# Preview Deploy

Self-hosted preview deployments for GitHub pull requests, on your own Tailscale tailnet.

Every PR gets a private URL like `pr-42-my-app.preview-server.<tailnet>.ts.net`, built from the PR's commit and torn down when the PR closes. Nothing is exposed on the public internet.

Status: the happy path works end to end. Teardown, limits and queueing, a management UI, and compose mode are planned but not built yet.

## How it works

1. An app repo calls the reusable workflow published from this repo (`.github/workflows/preview-deploy.yml`) on PR open, update, and close.
2. The workflow joins the tailnet and POSTs a deploy event to the server, authenticated with a per-app token.
3. The server clones the app at the PR's commit, builds it with its `Dockerfile`, and runs it on a private Docker network (`preview-net`).
4. `pr-{n}-{app}.preview-server.<tailnet>.ts.net` is routed to the container. TLS uses a Tailscale wildcard cert, renewed by the `ts-cert` sidecar.
5. A sticky PR comment shows the preview URL, updated on every push. Build failures are reported there too.
6. Closing or merging the PR tears the deployment down.

Fork PRs are skipped: fork code never runs on the tailnet.

Planned: caps and a deploy queue, a cleanup sweep with an age limit, compose-based apps, health checks, and a management UI.

## Setup

Needs a machine that can run Docker and join your tailnet (VPS or home box).

```bash
git clone git@github.com:filipkauffeldt/preview-deploy.git
cd preview-deploy
export TAILNET_DOMAIN=your-tailnet.ts.net
export TS_AUTHKEY=tskey-auth-...
export GITHUB_PAT=ghp_...
docker compose up -d
```

- `TAILNET_DOMAIN`: your tailnet's MagicDNS domain, e.g. `mytailnet.ts.net`
- `TS_AUTHKEY`: Tailscale auth key for the cert sidecar (tagged, ACL-limited)
- `GITHUB_PAT`: GitHub token used to read PR state and post comments

Register the first app via the seed variables in `compose.yaml` (`SEED__NAME`, `SEED__OWNER`, `SEED__REPO`, `SEED__TOKEN`). The token is the app's deploy token.

Then copy `templates/app-repo-workflow.yml` into the app repo as `.github/workflows/preview-deploy.yml`, fill in the app name, server URL, Tailscale OAuth client, and secrets (`PREVIEW_DEPLOY_TOKEN`, `TAILSCALE_OAUTH_CLIENT_SECRET`). The next PR gets a preview.

## Development

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

Tests use an in-memory SQLite database and a fake container runtime; no Docker daemon needed.

## Project layout

```
.github/workflows/          Reusable preview-deploy workflow + CI
templates/                  App repo workflow template
src/PreviewDeploy.Server/   The server: endpoints, deployments, routing, data
tests/PreviewDeploy.Server.Tests/
compose.yaml                Server + ts-cert sidecar
Dockerfile
```

## License

[GNU Affero General Public License v3.0](LICENSE)
