using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes.Swarm;

/// <summary>
/// The prod side of each resource, applied by <c>aspire publish</c> to write
/// infrastructure/docker/docker-compose.prod.yml (scripts/generate-prod-compose.ps1). Dokploy deploys
/// that file behind Traefik. Every value is a compose <c>${VAR}</c> with the names already set in
/// Dokploy, so the file holds no secrets and deploy.mjs keeps swapping API_IMAGE, WEB_IMAGE and
/// KEYCLOAK_IMAGE. docs/DEPLOYMENT.md lists the variables.
/// </summary>
internal static class ProdCompose
{
    private const string DokployNetwork = "dokploy-network";
    private const string AgentNetwork = "backup-agent";

    // Postgres and the backup agent only start with COMPOSE_PROFILES=selfhosted-db, set at the task 54 cutover.
    private const string SelfHostedDb = "selfhosted-db";

    // The images prod runs, as sha-<main commit> (CI publishes both on every push to main). Pinned here,
    // not in Dokploy, so a normal deploy never restarts the database and an upgrade is a reviewed PR.
    // Separate tags, so upgrading the agent doesn't restart Postgres. Both are the PR #35 merge commit.
    private const string PostgresTag = "sha-af85693a0235c8490b1ad2736ab30a89c04bf858";
    private const string BackupAgentTag = "sha-af85693a0235c8490b1ad2736ab30a89c04bf858";

    private static string Var(string name, string? fallback = null) =>
        fallback is null ? $"${{{name}}}" : $"${{{name}:-{fallback}}}";

    /// <summary>bash's /dev/tcp, because the Keycloak and API images have no curl.</summary>
    private static Healthcheck HttpHealthcheck(string path, int retries, string startPeriod) => new()
    {
        Test = ["CMD", "bash", "-c", $$"""exec 3<>/dev/tcp/localhost/8080 && printf 'GET {{path}} HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3 && head -1 <&3 | grep -q '200'"""],
        Interval = "10s",
        Timeout = "5s",
        Retries = retries,
        StartPeriod = startPeriod,
    };

    private static Deploy StartFirst(string delay) => new()
    {
        Replicas = 1,
        UpdateConfig = new UpdateConfig { Order = "start-first", FailureAction = "rollback", Delay = delay },
    };

    private static void Traefik(Service service, string router, string rule, int port, int? priority = null)
    {
        service.Labels["traefik.enable"] = "true";
        service.Labels[$"traefik.http.routers.{router}.rule"] = rule;
        if (priority is { } p)
        {
            service.Labels[$"traefik.http.routers.{router}.priority"] = p.ToString();
        }

        service.Labels[$"traefik.http.routers.{router}.entrypoints"] = "websecure";
        service.Labels[$"traefik.http.routers.{router}.tls.certresolver"] = "letsencrypt";
        service.Labels[$"traefik.http.services.{router}.loadbalancer.server.port"] = port.ToString();
    }

    /// <summary>Drops what Aspire derived from the dev resource: dev env, ports, bind mounts, build and depends_on.</summary>
    private static void Reset(Service service, string image)
    {
        service.Image = image;
        service.Build = null;
        service.Environment.Clear();
        service.Ports.Clear();
        service.Expose.Clear();
        service.DependsOn.Clear();
        service.Networks.Clear();
        service.Restart = "unless-stopped";
    }

    public static void File(ComposeFile file)
    {
        file.Name = "skoleoverblikket";
        file.Networks.Clear();
        file.Networks[DokployNetwork] = new Network { Name = DokployNetwork, External = true };
        file.Networks[AgentNetwork] = new Network { Name = AgentNetwork, Driver = "bridge" };

        // These volumes ARE the database. Aspire can't write a volume's `name:`, and without it compose
        // prefixes the project (Dokploy's app name), which would start an empty database. External keeps
        // the name exact, and `down -v` or Dokploy's "delete volumes" can't remove them. Create them once
        // per server before the first deploy (docs/DEPLOYMENT.md); compose refuses to start without them.
        foreach (var volume in file.Volumes.Values)
        {
            volume.External = true;
            volume.Driver = null; // compose rejects "external" together with "driver"
        }
    }

    /// <summary>
    /// No bootstrap admin: master realm admins are personal users with OTP, and kc.sh bootstrap-admin
    /// is the break-glass (docs/DEPLOYMENT.md). The master realm and its console answer 403 on the public
    /// host and are reached through an SSH tunnel to 127.0.0.1:8180. /admin/realms/* stays public,
    /// because the API calls the admin REST API there with its app-realm service account.
    /// </summary>
    public static void Keycloak(Service service)
    {
        Reset(service, Var("KEYCLOAK_IMAGE", "ghcr.io/nielspilgaard/skoleoverblikket-keycloak:latest"));
        service.Volumes.Clear(); // Realms and themes are baked into the prod image.
        service.Command = ["start", "--import-realm"];
        service.Healthcheck = HttpHealthcheck("/realms/master", retries: 18, startPeriod: "60s");
        service.Deploy = StartFirst("30s");
        service.Ports.Add("127.0.0.1:8180:8080");
        service.Environment["KC_DB"] = "postgres";
        service.Environment["KC_DB_URL"] = Var("KEYCLOAK_DB_URL");
        service.Environment["KC_DB_USERNAME"] = Var("KEYCLOAK_DB_USERNAME");
        service.Environment["KC_DB_PASSWORD"] = Var("KEYCLOAK_DB_PASSWORD");
        service.Environment["KC_HOSTNAME"] = "https://auth.skoleoverblikket.dk";
        service.Environment["KC_HOSTNAME_ADMIN"] = "http://localhost:8180";
        service.Environment["KC_PROXY_HEADERS"] = "xforwarded";
        service.Environment["KC_HTTP_ENABLED"] = "true";
        service.Environment["KC_HOSTNAME_STRICT"] = "false";
        service.Environment["KC_HOSTNAME_BACKCHANNEL_DYNAMIC"] = "true";
        service.Networks.Add(DokployNetwork);
        Traefik(service, "keycloak", "Host(`auth.skoleoverblikket.dk`)", 8080);
        // An allowlist only Traefik's own loopback matches, so every public request gets a 403.
        const string master = "traefik.http.routers.keycloak-master";
        service.Labels[$"{master}.rule"] = "Host(`auth.skoleoverblikket.dk`) && (PathPrefix(`/realms/master`) || PathPrefix(`/admin/master`))";
        service.Labels[$"{master}.priority"] = "100";
        service.Labels[$"{master}.entrypoints"] = "websecure";
        service.Labels[$"{master}.tls.certresolver"] = "letsencrypt";
        service.Labels[$"{master}.middlewares"] = "keycloak-master-deny";
        service.Labels[$"{master}.service"] = "keycloak";
        service.Labels["traefik.http.middlewares.keycloak-master-deny.ipallowlist.sourcerange"] = "127.0.0.1/32";
    }

    public static void Api(Service service)
    {
        Reset(service, Var("API_IMAGE", "ghcr.io/nielspilgaard/skoleoverblikket-api:latest"));
        service.Deploy = StartFirst("5s");
        string[] passThrough =
        [
            "Keycloak__WebClientId", "Keycloak__AdminClientId", "Keycloak__AdminClientSecret",
            "ObjectStorage__ServiceUrl", "ObjectStorage__AccessKey", "ObjectStorage__SecretKey",
            "ObjectStorage__DefaultBucketName", "ObjectStorage__PublicEndpoint", "ObjectStorage__PresignedUploadSigningKey",
            "Smtp__Username", "Smtp__Password",
            "Stripe__SecretKey", "Stripe__WebhookSecret", "Stripe__BasePriceIdMonthly", "Stripe__BasePriceIdYearly",
            "Stripe__ModulePriceIds__ParentModule__Monthly", "Stripe__ModulePriceIds__ParentModule__Yearly",
            "Stripe__ModulePriceIds__BoardModule__Monthly", "Stripe__ModulePriceIds__BoardModule__Yearly",
        ];
        service.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        service.Environment["ConnectionStrings__skoleoverblikket-db"] = Var("DATABASE_URL");
        service.Environment["Keycloak__Authority"] = "https://auth.skoleoverblikket.dk/realms/Skoleoverblikket";
        service.Environment["Keycloak__Audience"] = "skoleoverblikket-api";
        // Dokploy's variable is the config key with single underscores: Stripe_SecretKey for Stripe__SecretKey.
        foreach (var key in passThrough)
        {
            service.Environment[key] = Var(key.Replace("__", "_"));
        }

        service.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = Var("OTEL_EXPORTER_OTLP_ENDPOINT");
        service.Environment["OTEL_EXPORTER_OTLP_HEADERS"] = Var("OTEL_EXPORTER_OTLP_HEADERS");
        service.Environment["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        service.Environment["ElmahIo__ApiKey"] = Var("ElmahIo_ApiKey", "");
        service.Environment["ElmahIo__LogId"] = Var("ElmahIo_LogId", "");
        // Same endpoint the agent writes to, so the two can't drift apart.
        service.Environment["BackupStatus__ServiceUrl"] = Var("OpsBucket_ServiceUrl", "https://s3.rbx.io.cloud.ovh.net/");
        service.Environment["BackupStatus__AccessKey"] = Var("BackupStatus_AccessKey", "");
        service.Environment["BackupStatus__SecretKey"] = Var("BackupStatus_SecretKey", "");
        service.Healthcheck = HttpHealthcheck("/alive", retries: 12, startPeriod: "30s");
        service.Networks.Add(DokployNetwork);
        service.DependsOn["keycloak"] = new ServiceDependency { Condition = "service_started" };
        Traefik(service, "api", "Host(`skoleoverblikket.dk`) && PathPrefix(`/api`)", 8080, priority: 10);
    }

    public static void Web(Service service)
    {
        Reset(service, Var("WEB_IMAGE", "ghcr.io/nielspilgaard/skoleoverblikket-web:latest"));
        service.Deploy = StartFirst("5s");
        service.Healthcheck = new Healthcheck
        {
            Test = ["CMD", "curl", "-sf", "http://localhost:80"],
            Interval = "10s",
            Timeout = "5s",
            Retries = 6,
            StartPeriod = "10s",
        };
        service.Networks.Add(DokployNetwork);
        service.DependsOn["api"] = new ServiceDependency { Condition = "service_healthy" };
        Traefik(service, "web", "Host(`skoleoverblikket.dk`)", 80, priority: 1);
    }

    /// <summary>One cluster for the app and Keycloak databases (task 54 D2). No published port: only dokploy-network reaches it.</summary>
    public static void Postgres(Service service)
    {
        Reset(service, $"ghcr.io/nielspilgaard/skoleoverblikket-postgres:{PostgresTag}");
        service.Profiles = [SelfHostedDb];
        service.Command =
        [
            "postgres",
            "-c", "config_file=/etc/postgresql/postgresql.conf",
            "-c", $"shared_buffers={Var("PG_SHARED_BUFFERS", "512MB")}",
            "-c", $"effective_cache_size={Var("PG_EFFECTIVE_CACHE_SIZE", "1536MB")}",
        ];
        service.Environment["POSTGRES_PASSWORD"] = Var("POSTGRES_PASSWORD", "");
        service.Environment["APP_DB_PASSWORD"] = Var("APP_DB_PASSWORD", "");
        service.Environment["KEYCLOAK_DB_PASSWORD"] = Var("KEYCLOAK_DB_PASSWORD", "");
        service.Healthcheck = new Healthcheck
        {
            Test = ["CMD", "pg_isready", "-h", "/var/run/postgresql"],
            Interval = "10s",
            Timeout = "5s",
            Retries = 12,
            StartPeriod = "30s",
        };
        service.StopGracePeriod = "60s";
        service.Deploy = new Deploy { Resources = new Resources { Limits = new ResourceSpec { Memory = Var("POSTGRES_MEMORY", "2g") } } };
        service.Networks.Add(DokployNetwork);
    }

    /// <summary>
    /// Not on dokploy-network: it reaches Postgres only through the socket volume. The console is
    /// published on the host's 127.0.0.1 only, for the SSH tunnel. No depends_on: the console must
    /// come up when Postgres is broken.
    /// </summary>
    public static void BackupAgent(Service service)
    {
        Reset(service, $"ghcr.io/nielspilgaard/skoleoverblikket-backup-agent:{BackupAgentTag}");
        service.Profiles = [SelfHostedDb];
        service.Ports.Add("127.0.0.1:9090:9090");
        // The drill's tmpfs counts against the memory limit, so a drill can't starve Postgres.
        service.Tmpfs = [$"/drill:size={Var("DRILL_TMPFS_SIZE", "4g")},uid=999,gid=999,mode=0700"];
        service.Deploy = new Deploy { Resources = new Resources { Limits = new ResourceSpec { Memory = Var("BACKUP_AGENT_MEMORY", "5g") } } };
        service.StopGracePeriod = "30s";
        service.Environment["Agent__SshTunnelCommand"] = Var("BACKUP_CONSOLE_SSH", "");
        service.Environment["Agent__VolumeNamePrefix"] = "skoleoverblikket-pgdata-";
        foreach (var key in new[] { "S3_ENDPOINT", "S3_REGION", "S3_BUCKET", "S3_KEY", "S3_KEY_SECRET", "CIPHER_PASS" })
        {
            service.Environment[$"PGBACKREST_REPO1_{key}"] = Var($"PGBACKREST_REPO1_{key}", "");
        }

        foreach (var key in new[]
        {
            "OpsBucket__ServiceUrl", "OpsBucket__AccessKey", "OpsBucket__SecretKey",
            "Heartbeats__Wal", "Heartbeats__Backup", "Heartbeats__Drill", "Heartbeats__Verify",
            "Heartbeats__ElmahIo__ApiKey", "Heartbeats__ElmahIo__LogId",
        })
        {
            service.Environment[key] = Var(key.Replace("__", "_"), "");
        }

        // Not empty when unset: an empty enum fails binding, and an empty suffix would turn failures into successes.
        service.Environment["Heartbeats__Provider"] = Var("Heartbeats_Provider", "ElmahIo");
        service.Environment["Heartbeats__Ping__FailSuffix"] = Var("Heartbeats_Ping_FailSuffix", "/fail");

        service.Networks.Add(AgentNetwork);
    }
}
