const string label = "skoleoverblikket";

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL — the prod image (infrastructure/postgres): one cluster for the API and Keycloak databases,
// started on /pgdata/a or /pgdata/b by the backup agent's A/B marker. See infrastructure/backup-agent/README.md.
var pgUsername = builder.AddParameter("postgres-username", "postgres");
var pgPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", userName: pgUsername, password: pgPassword)
                      .WithDockerfile("../../postgres")
                      .WithLifetime(ContainerLifetime.Persistent)
                      // initdb/10-roles.sh creates the app and Keycloak roles with these passwords.
                      .WithEnvironment("APP_DB_PASSWORD", "dev-app")
                      .WithEnvironment("KEYCLOAK_DB_PASSWORD", "dev-keycloak")
                      // Small slot cap so the lost-slot case can be tried in minutes (4 GB on prod).
                      .WithArgs("postgres", "-c", "config_file=/etc/postgresql/postgresql.conf",
                                "-c", "max_slot_wal_keep_size=256MB", "-c", "shared_buffers=128MB")
                      .WithVolume("skoleoverblikket-pgdata-a", "/pgdata/a")
                      .WithVolume("skoleoverblikket-pgdata-b", "/pgdata/b")
                      .WithVolume("skoleoverblikket-pg-control", "/pg-control")
                      .WithVolume("skoleoverblikket-pg-socket", "/var/run/postgresql")
                      .WithPgAdmin(pgAdmin => pgAdmin.WithContainerRuntimeArgs(
                                       "--label",
                                       $"com.docker.compose.project={label}"))
                      .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}");

// Same database names as prod, created by initdb/10-roles.sh.
var db = postgres.AddDatabase("skoleoverblikket-db", databaseName: "skoleoverblikket");
postgres.AddDatabase("keycloak-db", databaseName: "keycloak");

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
                                           $"jdbc:postgresql://{postgres.Resource.Host}:{postgres.Resource.Port}/keycloak"))
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

// Silo (MinIO fork — S3-compatible local emulation for OVHCloud Object Storage)
var silo = builder.AddContainer("silo", "pgsty/silo", "RELEASE.2026-09-16T00-00-00Z")
                  .WithLifetime(ContainerLifetime.Persistent)
                  .WithVolume("skoleoverblikket-silo", "/data")
                  .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
                  .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
                  .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
                  .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin")
                  .WithArgs("server", "/data", "--console-address", ":9001")
                  .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}");
var siloS3 = silo.GetEndpoint("s3");

// Silo has no init hooks: a one-shot creates the backup agent's pgBackRest repo and ops buckets.
var siloBuckets = builder.AddContainer("silo-buckets", "pgsty/silo", "RELEASE.2026-09-16T00-00-00Z")
                         .WithEntrypoint("sh")
                         .WithArgs("-c", ReferenceExpression.Create(
                                       $"until mcli alias set local http://{siloS3.Property(EndpointProperty.Host)}:{siloS3.Property(EndpointProperty.Port)} minioadmin minioadmin; do sleep 1; done && mcli mb --ignore-existing local/pgbackrest local/skoleoverblikket-ops"))
                         .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={label}")
                         .WaitFor(silo);

// Backup agent (infrastructure/backup-agent): streams WAL and takes backups into Silo, runs the drill,
// and serves the restore console. Same image and volumes as prod; Silo stands in for the OVH buckets.
// No WaitFor(postgres), like prod: the console must come up when Postgres is broken.
const string consoleKey = "dev-console";
builder.AddDockerfile("backup-agent", "../../..", "infrastructure/backup-agent/Dockerfile")
       .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "console")
       .WithUrlForEndpoint("console", url =>
       {
           url.Url += $"/access?key={consoleKey}";
           url.DisplayText = "Backup console";
       })
       .WithVolume("skoleoverblikket-pg-socket", "/var/run/postgresql")
       .WithVolume("skoleoverblikket-pgdata-a", "/pgdata/a")
       .WithVolume("skoleoverblikket-pgdata-b", "/pgdata/b")
       .WithVolume("skoleoverblikket-pg-control", "/pg-control")
       .WithVolume("skoleoverblikket-wal-receive", "/wal-receive")
       .WithVolume("skoleoverblikket-backup-agent-state", "/var/lib/backup-agent")
       .WithContainerRuntimeArgs("--tmpfs", "/drill:size=1g,uid=999,gid=999,mode=0700",
                                 "--label", $"com.docker.compose.project={label}")
       .WithEnvironment("Agent__ConsoleKey", consoleKey)
       .WithEnvironment("Agent__VolumeNamePrefix", "skoleoverblikket-pgdata-")
       // Fast cadence so RPO and status changes show within minutes (5 minutes on prod).
       .WithEnvironment("Agent__WalSwitchInterval", "00:01:00")
       .WithEnvironment("Agent__StatusInterval", "00:01:00")
       .WithEnvironment("Agent__SlotWarnBytes", "134217728")
       // pgBackRest defaults to HTTPS; an http:// scheme on the endpoint turns that off.
       .WithEnvironment("PGBACKREST_REPO1_S3_ENDPOINT",
                        ReferenceExpression.Create($"http://{siloS3.Property(EndpointProperty.Host)}"))
       .WithEnvironment("PGBACKREST_REPO1_STORAGE_PORT", siloS3.Property(EndpointProperty.Port))
       .WithEnvironment("PGBACKREST_REPO1_S3_REGION", "us-east-1")
       .WithEnvironment("PGBACKREST_REPO1_S3_BUCKET", "pgbackrest")
       .WithEnvironment("PGBACKREST_REPO1_S3_KEY", "minioadmin")
       .WithEnvironment("PGBACKREST_REPO1_S3_KEY_SECRET", "minioadmin")
       .WithEnvironment("PGBACKREST_REPO1_CIPHER_PASS", "dev-cipher-pass")
       .WithEnvironment("OpsBucket__ServiceUrl", siloS3)
       .WithEnvironment("OpsBucket__AccessKey", "minioadmin")
       .WithEnvironment("OpsBucket__SecretKey", "minioadmin")
       .WaitForCompletion(siloBuckets);

// API — port 5000 is pinned so the Vite proxy target (http://127.0.0.1:5000) always resolves correctly.
var api = builder.AddProject<Projects.Skoleoverblikket_Api>("api")
                 .WithEndpoint("http", e => e.Port = 5000)
                 .WithReference(db)
                 .WithReference(keycloak.GetEndpoint("http"))
                 // The backoffice backup card reads the agent's status.json from the ops bucket.
                 .WithEnvironment("BackupStatus__ServiceUrl", siloS3)
                 .WithEnvironment("BackupStatus__AccessKey", "minioadmin")
                 .WithEnvironment("BackupStatus__SecretKey", "minioadmin")
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
