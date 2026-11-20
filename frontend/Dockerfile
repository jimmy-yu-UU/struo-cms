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
# The runtime image is the official nginx image. nginx runs as its `nginx`
# user (uid 101), not root, and listens on 8080, an unprivileged port.
# nginx/nginx.conf has no `user` directive and keeps the pid file and temp
# directories under /tmp. /etc/nginx, /etc/nginx/conf.d and /var/cache/nginx
# belong to uid 101 with group 0 and are group-writable, so the entrypoint
# renders the template into /etc/nginx/conf.d as uid 101 or as any uid in
# group 0. The base image's own conf.d/default.conf (port 80) is removed;
# the rendered template is the only site config.
#
# The runtime stage runs `apk upgrade` as root, then switches to uid 101.
# Alpine publishes package fixes before the upstream image is rebuilt, and
# this keeps every build on the current fixes. It is permanent, not a
# workaround for a particular CVE: when the base image already carries the
# fixes it upgrades nothing.
#
# One deliberate asymmetry with the API image:
# - No HEALTHCHECK: unlike the API image, static nginx here has no downstream
#   dependency to probe (no database, no cache) -- an orchestrator supervising
#   this container should probe `/` directly instead.
FROM nginx:1.30.7-alpine AS runtime
RUN apk upgrade --no-cache \
    && rm -f /etc/nginx/conf.d/default.conf \
    && chown 101:0 /etc/nginx /etc/nginx/conf.d /var/cache/nginx \
    && chmod 775 /etc/nginx /etc/nginx/conf.d /var/cache/nginx
COPY --chown=101:0 --chmod=664 nginx/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://api:8080
ENV HSTS_VALUE=
ENV CSP_IMG_EXTRA=
ENV CSP_CONNECT_EXTRA=
ENV NGINX_RESOLVER=127.0.0.11
USER 101
EXPOSE 8080
