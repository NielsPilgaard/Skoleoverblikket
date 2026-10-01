// Renders the marketing pages to static HTML after `vite build`, so crawlers get real text,
// per-page <title>, description and canonical without running JavaScript.
// Output: dist/prerendered/index.html for "/" and dist/prerendered/<route>.html for the rest.
// nginx.conf serves these; every other route keeps the empty SPA shell (dist/index.html).
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const webDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const distDir = path.join(webDir, 'dist')
const outDir = path.join(distDir, 'prerendered')

const { render, PRERENDERED_PATHS } = await import(
  pathToFileURL(path.join(webDir, 'dist-ssr', 'entry-server.js')).href
)

const template = fs.readFileSync(path.join(distDir, 'index.html'), 'utf-8')
const shellHeadTags = [/<title>[\s\S]*?<\/title>/, /<meta name="description"[^>]*>/]
for (const re of shellHeadTags) {
  if (!re.test(template)) throw new Error(`prerender: ${re} not found in dist/index.html`)
}
if (!template.includes('<div id="root"></div>')) {
  throw new Error('prerender: <div id="root"></div> not found in dist/index.html')
}

fs.mkdirSync(outDir, { recursive: true })

for (const url of PRERENDERED_PATHS) {
  const { html, head } = render(url)
  if (!html.includes('<h1')) throw new Error(`prerender: ${url} rendered without an <h1>`)

  let page = template
  for (const re of shellHeadTags) page = page.replace(re, '')
  page = page
    .replace('</head>', `${head}\n</head>`)
    .replace('<div id="root"></div>', `<div id="root">${html}</div>`)

  const file = url === '/' ? 'index.html' : `${url.slice(1)}.html`
  fs.writeFileSync(path.join(outDir, file), page)
  console.log(`prerendered ${url} -> dist/prerendered/${file}`)
}
