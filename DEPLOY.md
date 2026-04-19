# Deploying a Razorshave SPA

`dotnet build -c Razorshave <project>` produces a self-contained `dist/`:

```
dist/
├── index.html                 Entry point — loads CSS + the bundle
├── main.<hash>.js             Transpiled components + runtime, minified, tree-shaken
├── app.css                    Your project's wwwroot/app.css
├── <Project>.styles.css       Scoped-CSS bundle Razor emits under obj/
├── lib/                       Bootstrap + anything else from wwwroot/lib
├── favicon.png
├── 404.html                   Copy of index.html (GitHub Pages fallback)
├── _redirects                 Netlify / Cloudflare Pages SPA fallback
├── serve.json                 `npx serve` SPA fallback
└── web.config                 IIS / Azure Static Web Apps rewrite rules
```

The four config files (`404.html`, `_redirects`, `serve.json`, `web.config`) exist so any of the common static hosts can route unknown paths to `index.html` — that's what lets a direct `GET /weather` reach the client router.

## Per-host notes

### Local dev — `npx serve`

```bash
npx --yes serve dist
```

`serve.json` triggers the single-page-app mode automatically; you can also pass `-s` explicitly.

### nginx

nginx needs server-level config (no file in the bundle can cover this):

```nginx
server {
  listen 80;
  root /var/www/your-app/dist;
  index index.html;

  location / {
    try_files $uri $uri/ /index.html;
  }

  # Long-cache the hashed bundle, short-cache everything else
  location ~* \.(js|css|png|ico|woff2?)$ {
    expires 1y;
    add_header Cache-Control "public, immutable";
  }
  location = /index.html {
    add_header Cache-Control "no-cache";
  }
}
```

### Apache

`.htaccess` in the deployed `dist/`:

```apache
<IfModule mod_rewrite.c>
  RewriteEngine On
  RewriteCond %{REQUEST_FILENAME} !-f
  RewriteCond %{REQUEST_FILENAME} !-d
  RewriteRule . /index.html [L]
</IfModule>
```

(Razorshave does not emit this file by default — drop it in if you host on Apache.)

### Cloudflare Pages

Drop the `dist/` as-is. Cloudflare Pages picks up `_redirects` automatically. No console config needed.

### Netlify

Same as Cloudflare — `_redirects` is picked up automatically. `netlify.toml` works too if you prefer build metadata:

```toml
[[redirects]]
  from = "/*"
  to = "/index.html"
  status = 200
```

### IIS / Azure Static Web Apps

`web.config` is picked up automatically by IIS. For Azure Static Web Apps, `staticwebapp.config.json` is preferred; drop one alongside `dist/` if needed:

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/main.*.js", "/app.css", "/lib/*", "/favicon.png"]
  }
}
```

### GitHub Pages

Drop `dist/` into the repo's `docs/` folder (or configure Pages to serve from a branch). The auto-generated `404.html` handles all unknown paths — GitHub Pages serves it with HTTP 200 and the client router reads `window.location.pathname` to match the right route.

### AWS S3 + CloudFront

S3 alone doesn't route — you need a CloudFront distribution. In the CloudFront "Error Pages" (or "Custom Error Responses") section:

| Error code | Response code | Response page | TTL |
|---|---|---|---|
| 403 | 200 | `/index.html` | 0 |
| 404 | 200 | `/index.html` | 0 |

Terraform / CDK equivalents live in the respective provider docs.

### Docker / Kubernetes (nginx image)

```dockerfile
FROM nginx:1.27-alpine
COPY dist/ /usr/share/nginx/html/
COPY nginx.conf /etc/nginx/conf.d/default.conf
```

Where `nginx.conf` is the snippet from the nginx section above.

## Caching strategy

The bundle filename is content-hashed (`main.<8-char-hash>.js`), so caching is safe and cheap:

- `main.*.js`, `*.styles.css`, `lib/*` → `Cache-Control: public, immutable, max-age=31536000`
- `index.html`, `404.html` → `Cache-Control: no-cache` (always fetch fresh so hash references update)

## Troubleshooting

- **"Direct link to `/weather` 404s"** → missing SPA fallback on your host. Use one of the configs above.
- **"Stylesheets not loading"** → check the `<link>` tags in `index.html` resolve relative to your deploy path. If you serve under `/app/`, CSS links like `app.css` resolve to `/app/app.css` which works.
- **"`main.abc123.js` 404 after deploy"** → cached older `index.html` still referencing the old hash. Set `no-cache` on `index.html`.
- **"CORS errors from the API call"** → your API needs to set CORS headers, or use a same-origin proxy. Not a Razorshave concern.
