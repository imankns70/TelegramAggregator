import { copyFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const configuredUrl = process.env.API_BASE_URL;
if (!configuredUrl) {
    throw new Error('Set API_BASE_URL in Netlify to the HTTPS origin of the C# backend.');
}

const apiUrl = new URL(configuredUrl);
if (apiUrl.protocol !== 'https:' || apiUrl.username || apiUrl.password
    || apiUrl.pathname !== '/' || apiUrl.search || apiUrl.hash) {
    throw new Error('API_BASE_URL must be an HTTPS origin without credentials, path, query or fragment.');
}

const output = resolve('dist/dashboard');
const source = resolve('src/TelegramAggregator.WebAPI/wwwroot');
await mkdir(output, { recursive: true });
await copyFile(resolve(source, 'debug.html'), resolve(output, 'index.html'));
await copyFile(resolve(source, 'debug.html'), resolve(output, 'debug.html'));
await copyFile(resolve(source, 'preview.html'), resolve(output, 'preview.html'));
await writeFile(resolve(output, '_redirects'),
    `/api/* ${apiUrl.origin}/api/:splat 200\n/health/* ${apiUrl.origin}/health/:splat 200\n`);
console.log('Dashboard built with backend proxy:', apiUrl.origin);
