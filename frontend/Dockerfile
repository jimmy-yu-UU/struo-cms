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
FROM node:24.22.0-alpine AS build
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
# behavior; API_UPSTREAM, HSTS_VALUE, NGINX_RESOLVER, CSP_IMG_EXTRA and
# CSP_CONNECT_EXTRA are consumed by nginx's own envsubst-on-templates
# entrypoint at container start.
#
# The runtime image is nginxinc/nginx-unprivileged: nginx runs as its
# non-root `nginx` user (uid 101) and listens on 8080, an unprivileged port, so
# there is no USER directive to add here. The image's entrypoint renders the
# template into /etc/nginx/conf.d as that same user, and its pid file and temp
# directories are under /tmp.
#
# One deliberate asymmetry with the API image:
# - No HEALTHCHECK: unlike the API image, static nginx here has no downstream
#   dependency to probe (no database, no cache) -- an orchestrator supervising
#   this container should probe `/` directly instead.
FROM nginxinc/nginx-unprivileged:1.30.6-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://api:8080
ENV HSTS_VALUE=
ENV CSP_IMG_EXTRA=
ENV CSP_CONNECT_EXTRA=
ENV NGINX_RESOLVER=127.0.0.11
EXPOSE 8080
