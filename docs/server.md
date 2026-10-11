# Monolith Harness Server

**This server is optional.** It lets you add cloud and collaborative features on premise, on a Windows or Linux machine you control. Desktop, CLI and Android continue to work locally without it. In version 1.91.0, Desktop and CLI can connect to the server; Android integration is not available yet.

## What it provides

The standalone .NET executable hosts a Blazor/MudBlazor web interface, a REST API with OpenAPI/Swagger, a WebSocket channel and the shared Monolith engine in worker processes. No separate .NET installation is needed for the downloads.

Administrators enable features and grant access through accounts, custom roles and project permissions.

| Feature | Purpose | Default |
| --- | --- | --- |
| Cloud execution | Run conversations and tools on the server, inside a project workspace. | Off |
| Live sharing | Let invited people follow and comment on a local conversation. Tools keep running on the host device. | On |
| Cloud schedules | Run scheduled requests without a connected client, using a selected time zone. | Off |
| Request queue | Limit simultaneous users/runs and queue extra requests. | On |
| Shared providers | Offer selected models to users or roles. Provider keys stay on the server. | On |

| Mode | Where tools run | Where the conversation stays |
| --- | --- | --- |
| Local | On your device | On your device |
| Cloud | On the server | On the server, reflected in connected Desktop/CLI clients |
| Shared local | On the host device | On the host device; invited viewers receive live events through the server |

Viewers can read and comment; they cannot control tools or approve commands. Live history is not retained by the server after the host disconnects. The host can revoke sharing; viewers can leave.

## First start

1. Download the [Windows x64 or Linux x64 archive](https://github.com/loicdelaunay/Monolith-Harness/releases/tag/server-v1.91.0) and extract it into a writable, private folder.
2. Run `MonolithHarness.exe` on Windows or `./MonolithHarness` on Linux.
3. Open `http://127.0.0.1:5800` in a browser. The administrator account is initialized once; restarting does not reset it.
4. Set your own administrator password before allowing remote access. With local console access, `MonolithHarness reset-password --email admin@admin.com` issues a temporary recovery code. Use **Recovery code** on the sign-in page to choose a password, then sign in.
5. Create user accounts, configure providers and permissions, then enable the features you need in administration settings.

The server starts with loopback access only. Its data is portable: `database.sqlite`, `server.json` and `workspace/` live beside the executable. `--data <folder>` selects another data folder. Run recovery commands with the same data folder and OS account as the server.

```powershell
# Windows: localhost, custom port
.\MonolithHarness.exe --port 8080
```

```bash
# Linux: localhost, default port 5800
./MonolithHarness
```

## Configure remote access

Use HTTPS for remote clients, either with a TLS reverse proxy or a PKCS#12 certificate. `--host 0.0.0.0` explicitly enables listening on all IPv4 interfaces. Expose only the intended port through your firewall.

| Setting | Use |
| --- | --- |
| `--host <address>`, `--port <number>` | Listening address and HTTP port; defaults are `127.0.0.1` and `5800`. |
| `--https-port <number>`, `--cert <file.pfx>` | Add a direct HTTPS listener. Supply the certificate password through `MONOLITH_SERVER_CERT_PASSWORD`. |
| `--behind-proxy` | Accept forwarded headers from a TLS reverse proxy; restrict direct access to that proxy. |
| `--redirect-https` | Redirect HTTP requests to the configured direct HTTPS listener. |
| `--data <folder>` | Choose the private folder holding the server data. |

Settings are read in this order: defaults, `server.json`, `MONOLITH_SERVER_*` environment variables, then command-line options. The initial `server.json` is created automatically.

## Connect Desktop or CLI

- **Desktop:** open **Settings → Server**, save the server URL, sign in and synchronize shared providers. Open or create a cloud project there. Use a conversation's context menu to share a local conversation live.
- **CLI:** use `/server`, or the `server status|connect|login|logout|sync|projects|open|new|forget` commands. `run "..." --cloud <project>` submits a request to a cloud project. Use `--password-stdin` for sign-in without putting a password in command history.
- HTTPS is the default for remote connections. Plain HTTP requires explicit opt-in; loopback is allowed for local setup.
- Shared providers can be enabled for local requests too. Clients store a protected session token; the upstream provider key remains on the server.

## Approvals and execution limits

An action requires its feature to be enabled, a suitable role permission and access to the resource. Tool approvals go to the user who started the request. Scheduled work that needs approval waits for that user instead of approving itself.

Cloud workers use a separate project workspace and engine database. Code execution requires Docker or Podman with container isolation by default; a required but unavailable container runtime causes the run to fail. Workers are refused under an administrator account by default. Administrators configure time, CPU, memory, disk, network and queue limits.

**Deployment limits:** resource limits are monitored by the server; they do not use cgroups or Windows Job Objects. Workers run under the server's OS account, so strong isolation depends on the container. This release's container execution path has not been validated end to end on the development machine. There is no public signup, email delivery or per-user provider spending quota.

## API and maintenance

- REST: `/api/...`; OpenAPI: `/openapi/v1.json`; Swagger: `/swagger`.
- Real-time updates: `/ws`, for streamed replies, queue progress, questions, approvals and sharing.
- Provider relay: `/api/providers/{id}/relay/{chat/completions|embeddings|models}`. Access follows the provider permissions.
- Administration includes an audit log, session revocation and database backups. For a complete backup, stop the server and copy **the entire data folder**, including `workspace/keys` and project files. The built-in backup covers SQLite only. Windows encryption keys are tied to the OS account; Linux data folders must remain private.
- Recovery: `MonolithHarness reset-password --email <address>` issues a temporary recovery code. `MonolithHarness restore --from <backup.sqlite>` restores SQLite while the server is stopped, preserving the previous database.
- To update, stop the server and replace only its executable. Keep `database.sqlite`, `server.json` and `workspace/`.

## Build from source

```powershell
.\publish-server.ps1 -OutputDirectory artifacts/SERVER
```

```bash
dotnet publish src/MonolithHarness.Server -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/SERVER
```

For implementation details, see the [Server project guide](../src/MonolithHarness.Server/README.md). This simplified guide is based on the [Server functional specification](https://docs.google.com/document/d/1CrxFn7qZEggCiJDOZqKe_GBVc0wd_WdJbi3rauypgWo/edit), with current implementation limits stated above.
