const string label = "skoleoverblikket";

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL — shared server, separate databases for API and Keycloak
var pgUsername = builder.AddParameter("postgres-username", "postgres");
var pgPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", userName: pgUsername, password: pgPassword)
                      .WithLifetime(ContainerLifetime.Persistent)
                      .WithVolume("skoleoverblikket-pgdata", "/var/lib/postgresql")
                      .WithPgAdmin(pgAdmin => pgAdmin.WithContainerRuntimeArgs(
                                       "--label",
                                       $"com.docker.compose.project={label}"))
                      .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}");

var db = postgres.AddDatabase("skoleoverblikket-db");
postgres.AddDatabase("keycloak-db");

// Mailpit — local SMTP catch-all (captures all outbound email in dev)
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "latest")
                     .WithLifetime(ContainerLifetime.Persistent)
                     .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "ui")
                     .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp", scheme: "tcp")
                     .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}");

// Keycloak (raw container — no first-party Aspire hosting package)
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.2")
                      .WithLifetime(ContainerLifetime.Persistent)
                      .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
                      .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
                      .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD",
                                       builder.AddParameter("keycloak-admin-password", secret: true))
                      .WithEnvironment("KC_DB", "postgres")
                      .WithEnvironment("KC_DB_USERNAME", pgUsername)
                      .WithEnvironment("KC_DB_PASSWORD", pgPassword)
                      .WithEnvironment("KC_DB_URL",
                                       ReferenceExpression.Create(
                                           $"jdbc:postgresql://{postgres.Resource.Host}:{postgres.Resource.Port}/keycloak-db"))
                      .WithBindMount("../../../infrastructure/keycloak/realms",
                                     "/opt/keycloak/data/import",
                                     isReadOnly: true)
                      .WithBindMount("../../../infrastructure/keycloak/themes",
                                     "/opt/keycloak/themes",
                                     isReadOnly: true)
                      .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}")
                      .WithArgs("start-dev", "--import-realm")
                      .WaitFor(postgres)
                      .WaitFor(mailpit);

// LocalStack (S3-compatible local emulation for OVHCloud Object Storage)
builder.AddContainer("localstack", "localstack/localstack", "3")
       .WithLifetime(ContainerLifetime.Persistent)
       .WithVolume("skoleoverblikket-s3", "/var/lib/localstack/data")
       .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "gateway")
       .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}");

// API — port 5000 is pinned so the Vite proxy target (http://127.0.0.1:5000) always resolves correctly.
var api = builder.AddProject<Projects.Skoleoverblikket_Api>("api")
                 .WithEndpoint("http", e => e.Port = 5000)
                 .WithReference(db)
                 .WithReference(keycloak.GetEndpoint("http"))
                 .WaitFor(db)
                 .WaitFor(keycloak)
                 .WithUrlForEndpoint("http", url =>
             {
                 url.Url += "/api/v1/openapi";
                 url.DisplayText = "Swagger UI";
             });

// Stripe CLI — forwards real Stripe test-mode webhook events to the local API automatically.
// Uses the committed test-mode secret key (appsettings.Development.json Stripe:SecretKey) via
// STRIPE_API_KEY, so no interactive `stripe login` is needed. The webhook signing secret is
// deterministic for a given API key, and already matches Stripe:WebhookSecret in the committed
// dev config — acts as if `stripe listen` were always running, no manual steps.
var stripeTestKey = builder.AddParameter("stripe-secret-key", "sk_test_51TITT8RkFVB6gS8XwoPOFAkHF8okYmkdNtrfgrKz6tTlgthgs6oMrWuiEncXGYiOgDkVS02khiyLVVOUoaBuIXnY00aNUTerrR");

builder.AddContainer("stripe-listen", "stripe/stripe-cli", "latest")
       .WithLifetime(ContainerLifetime.Persistent)
       .WithEnvironment("STRIPE_API_KEY", stripeTestKey)
       .WithArgs(
           "listen",
           "--forward-to", "http://host.docker.internal:5000/api/v1/stripe/webhook",
           "--skip-verify")
       .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}")
       .WaitFor(api);

// React + Vite frontend
builder.AddViteApp("web", appDirectory: "../../../web", runScriptName: "dev")
       .WithEndpoint("http", e => e.Port = 5173)
       .WithExternalHttpEndpoints()
       .WithReference(api);

builder.Build().Run();
