# Admin SPA image: build the Vue 3 app with pnpm, serve the static output
# behind nginx.
#
# pnpm's version is not pinned here -- it comes from the "packageManager"
# field in package.json, which corepack reads and installs to match. Do not
# hand-pin a pnpm version in this file; package.json is the single source of
# truth.
# --ignore-scripts on install matches the CI frontend job's reasoning:
# untrusted package postinstall scripts don't get to run during image build.
# Node 24 is used to match the CI "frontend" job's runtime.
FROM node:24-alpine AS build
WORKDIR /app
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable
# pnpm-workspace.yaml is copied alongside the lockfile too: it (not
# package.json) is where this project's dependency `overrides` live. Without
# it present, --frozen-lockfile fails with ERR_PNPM_LOCKFILE_CONFIG_MISMATCH
# because the lockfile's recorded overrides have nothing to match against.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile --ignore-scripts
COPY . .
RUN pnpm build

# Runtime: nginx serves the built dist/ and reverse-proxies /api/ to the
# backend. See nginx/default.conf.template for the proxy and header
# behavior; API_UPSTREAM, HSTS_VALUE and NGINX_RESOLVER are consumed by
# nginx's own envsubst-on-templates entrypoint at container start.
#
# Two deliberate asymmetries with the API image, neither a behaviour change:
# - No USER directive: the stock nginx:alpine image's master process needs to
#   run as root to bind the privileged port 80 (its worker processes still
#   drop to the unprivileged `nginx` user on their own). Switching to
#   nginxinc/nginx-unprivileged would drop that root requirement but also
#   changes the exposed port away from 80, which this image's EXPOSE/runtime
#   contract does not do.
# - No HEALTHCHECK: unlike the API image, static nginx here has no downstream
#   dependency to probe (no database, no cache) -- an orchestrator supervising
#   this container should probe `/` directly instead.
FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://api:8080
ENV HSTS_VALUE=
ENV NGINX_RESOLVER=127.0.0.11
EXPOSE 80
