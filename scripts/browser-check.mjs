import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, cp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import assert from 'node:assert/strict';

const root = path.resolve(import.meta.dirname, '..');
const scratch = await mkdtemp(path.join(tmpdir(), 'forum-browser-'));
const data = path.join(scratch, 'data');
const release = path.join(scratch, 'release');
const artifactPath = path.join(root, 'artifacts');
const dotnet = process.env.DOTNET_COMMAND || 'dotnet';
const password = `Test!${randomBytes(15).toString('hex')}`;
let server;
let browser;
const checks = [];
const errors = [];
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', DataDirectory: data,
  Admin__UserName: 'BrowserAdmin', Admin__Email: 'browser@example.test', Admin__Password: password,
  Email__Mode: 'Disabled', Logging__LogLevel__Default: 'Warning' };

async function command(executable, args, options = {}) {
  const child = spawn(executable, args, { env, ...options });
  let output = '';
  child.stdout?.on('data', b => output += b);
  child.stderr?.on('data', b => output += b);
  const code = await new Promise((resolve, reject) => { child.on('exit', resolve); child.on('error', reject); });
  assert.equal(code, 0, `${executable} failed: ${output}`);
  return output;
}
async function freePort() {
  const probe = createServer();
  await new Promise(resolve => probe.listen(0, '127.0.0.1', resolve));
  const port = probe.address().port;
  await new Promise(resolve => probe.close(resolve));
  return port;
}
const port = await freePort();
const url = `http://127.0.0.1:${port}`;
async function start(directory) {
  const child = spawn(dotnet, [path.join(directory, 'OnlineDisscussionForum.dll'), '--urls', url], { cwd: directory, env });
  let log = '';
  child.stdout.on('data', b => log += b);
  child.stderr.on('data', b => log += b);
  let ended = false; child.on('exit', () => ended = true);
  child.on('error', err => { log += err.message; ended = true; });
  for (let n = 0; n < 150; n++) {
    if (ended) throw Error(`Server stopped: ${log}`);
    try { if ((await fetch(`${url}/health`)).ok) return child; } catch {}
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw Error(`Server readiness timed out: ${log}`);
}
async function stop(child) {
  if (!child || child.exitCode !== null) return;
  const exited = new Promise(resolve => child.on('exit', resolve));
  child.kill('SIGTERM'); await exited;
}
async function go(page, target) {
  const response = await page.goto(`${url}${target}`);
  assert.equal(response.status(), 200, target);
}
async function submit(page, label) {
  await Promise.all([page.waitForURL(u => !u.pathname.endsWith('/Create') && !u.pathname.includes('/Edit'), { timeout: 15000 }), page.getByRole('button', { name: label, exact: true }).click()]);
}
try {
  await mkdir(artifactPath, { recursive: true });
  await command(dotnet, ['publish', path.join(root, 'OnlineDisscussionForum FYP/OnlineDisscussionForum/OnlineDisscussionForum.csproj'), '-c', 'Release', '-o', release, '--nologo'], { cwd: root });
  await command(dotnet, [path.join(release, 'OnlineDisscussionForum.dll'), '--migrate'], { cwd: release });
  await command(dotnet, [path.join(release, 'OnlineDisscussionForum.dll'), '--migrate'], { cwd: release });
  server = await start(release);
  browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
  const context = await browser.newContext({ extraHTTPHeaders: { 'X-Forwarded-Proto': 'https' }, viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  page.on('pageerror', err => errors.push(err.message));
  page.on('dialog', async dialog => { errors.push(`Unexpected script dialog: ${dialog.message()}`); await dialog.dismiss(); });
  await go(page, '/Account/Login');
  await page.locator('#UserName').fill('BrowserAdmin'); await page.locator('#Password').fill(password);
  await Promise.all([page.waitForURL(`${url}/`), page.getByRole('button', { name: 'Log in', exact: true }).click()]);
  assert.ok((await context.cookies()).some(c => c.name.includes('Identity.Application') && c.secure && c.httpOnly));
  checks.push('Production login with secure cookie and email disabled');
  await go(page, '/Forum/Create');
  await page.locator('#Title').fill('Browser forum'); await page.locator('#Description').fill('Discussion');
  await Promise.all([page.waitForURL(`${url}/Forum`), page.getByRole('button', { name: 'Create Forum', exact: true }).click()]);
  await page.getByRole('link', { name: 'Browser forum', exact: true }).click();
  const forumUrl = new URL(page.url()).pathname;
  const forumId = forumUrl.split('/').at(-1);
  await go(page, `/Post/Create/${forumId}`);
  await page.locator('#Title').fill('Browser post'); await page.locator('#Content').fill('<script>alert("xss")</script>\nSecond line');
  await Promise.all([page.waitForURL(/\/Post\/Index\//), page.getByRole('button', { name: 'Submit Post', exact: true }).click()]);
  const postUrl = new URL(page.url()).pathname;
  const postId = postUrl.split('/').at(-1);
  assert.equal(await page.locator('.postContent').textContent().then(t => t.includes('<script>')), true);
  await page.getByRole('link', { name: 'Edit post', exact: true }).click();
  await page.locator('#Title').fill('Edited browser post'); await page.locator('#Content').fill('Edited body\nSecond line');
  await Promise.all([page.waitForURL(`${url}${postUrl}`), page.getByRole('button', { name: 'Save changes', exact: true }).click()]);
  await page.getByRole('link', { name: 'Post Reply', exact: true }).click(); await page.locator('#ReplyContent').fill('Browser reply');
  await Promise.all([page.waitForURL(`${url}${postUrl}`), page.getByRole('button', { name: 'Submit Reply', exact: true }).click()]);
  await page.getByRole('link', { name: 'Edit reply', exact: true }).click(); await page.locator('#Content').fill('Edited reply');
  await Promise.all([page.waitForURL(`${url}${postUrl}`), page.getByRole('button', { name: 'Save changes', exact: true }).click()]);
  await page.getByRole('link', { name: 'Delete reply', exact: true }).click();
  await Promise.all([page.waitForURL(`${url}${postUrl}`), page.getByRole('button', { name: 'Confirm deletion', exact: true }).click()]);
  checks.push('Forum create and post/reply CRUD through rendered forms; text is encoded');
  await page.screenshot({ path: path.join(artifactPath, 'discussion-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Toggle navigation' }).click();
  await page.getByRole('link', { name: 'Forums', exact: true }).waitFor({ state: 'visible' });
  await page.locator('#forum-navigation.in').waitFor({ state: 'visible' });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 2), 'Mobile page overflows viewport');
  await page.screenshot({ path: path.join(artifactPath, 'navigation-mobile.png'), fullPage: true });
  await page.getByRole('button', { name: 'Toggle navigation' }).click();
  await page.waitForFunction(() => document.querySelector('#forum-navigation').className === 'navbar-collapse collapse');
  await page.screenshot({ path: path.join(artifactPath, 'discussion-mobile.png'), fullPage: true });
  checks.push('Desktop and mobile layout; mobile navigation is usable');
  await page.setViewportSize({ width: 1440, height: 1000 });
  await go(page, `/Forum/Edit/${forumId}`); await page.locator('#Description').fill('Edited discussion');
  await Promise.all([page.waitForURL(`${url}${forumUrl}`), page.getByRole('button', { name: 'Save changes', exact: true }).click()]);
  await go(page, `/Forum/Delete/${forumId}`); await page.getByRole('button', { name: 'Confirm deletion', exact: true }).click();
  await page.getByText('This forum contains posts or has changed.', { exact: false }).waitFor();
  checks.push('Nonempty forum deletion returns a useful form error');
  await go(page, '/Manage/Index');
  assert.equal(await page.getByRole('button', { name: 'Send verification email' }).count(), 0);
  await go(page, '/Account/ForgotPassword'); assert.equal(await page.locator('form[asp-action="ForgotPassword"], form[action="/Account/ForgotPassword"]').count(), 0);
  checks.push('Production email workflows are hidden/disabled');
  const profileLink = await page.locator('a[href^="/Profile/Detail/"]').first().getAttribute('href');
  await go(page, profileLink);
  await page.locator('input[name="file"]').setInputFiles(path.join(root, 'OnlineDisscussionForum FYP/OnlineDisscussionForum/wwwroot/images/forum/default.png'));
  await Promise.all([page.waitForResponse(r => r.url().endsWith('/Profile/UploadProfileImage')), page.getByRole('button', { name: 'Submit', exact: true }).click()]);
  await page.waitForLoadState('networkidle');
  const imageStyle = await page.locator('#userProfileImage').getAttribute('style');
  const imageUrl = imageStyle.match(/url\(([^)]+)\)/)[1].replaceAll('"', '');
  assert.ok(imageUrl.startsWith('/uploads/'));
  assert.equal((await page.request.get(`${url}${imageUrl}`)).status(), 200);
  const dataPermissions = await command('python3', ['-c', 'import os,sys; print(oct(os.stat(sys.argv[1]).st_mode & 0o777))', data]);
  assert.equal(dataPermissions.trim(), '0o700');
  checks.push('Validated local uploads and private data directory permissions');
  const userCookie = (await context.cookies()).filter(c => c.name.includes('Identity.Application'));
  await command('python3', [path.join(root, 'scripts/sqlite-backup.py'), 'backup', path.join(data, 'forum.db'), path.join(scratch, 'backup.db')]);
  const keysBefore = await command('python3', ['-c', 'from pathlib import Path; import hashlib,sys; print(sorted((p.name,hashlib.sha256(p.read_bytes()).hexdigest()) for p in Path(sys.argv[1]).glob("*.xml")))', path.join(data, 'keys')]);
  await stop(server); server = null;
  const nextRelease = path.join(scratch, 'release-next'); await cp(release, nextRelease, { recursive: true });
  server = await start(nextRelease);
  await context.addCookies(userCookie); await go(page, '/Manage/Index');
  await go(page, postUrl); assert.ok((await page.textContent('body')).includes('Edited browser post'));
  assert.equal((await page.request.get(`${url}${imageUrl}`)).status(), 200);
  const keysAfter = await command('python3', ['-c', 'from pathlib import Path; import hashlib,sys; print(sorted((p.name,hashlib.sha256(p.read_bytes()).hexdigest()) for p in Path(sys.argv[1]).glob("*.xml")))', path.join(data, 'keys')]);
  assert.equal(keysBefore, keysAfter);
  checks.push('New release directory and process restart preserve users, content, images, keys, and authentication');
  await stop(server); server = null;
  await command('python3', [path.join(root, 'scripts/sqlite-backup.py'), 'restore', path.join(scratch, 'backup.db'), path.join(data, 'restored.db'), '--service-stopped']);
  env.ConnectionStrings__DefaultConnection = `Data Source=${path.join(data, 'restored.db')}`;
  server = await start(nextRelease); await go(page, postUrl);
  assert.ok((await page.textContent('body')).includes('Edited browser post'));
  checks.push('Verified online backup and offline restore into a new SQLite file');
  const response = await page.request.get(`${url}/forum.db`); assert.equal(response.status(), 404);
  const noMail = await page.request.post(`${url}/Account/ForgotPassword`, { form: { Email: 'browser@example.test' } }); assert.equal(noMail.status(), 400);
  assert.deepEqual(errors, []);
  await writeFile(path.join(artifactPath, 'browser-report.json'), JSON.stringify({ checks, errors, date: new Date().toISOString() }, null, 2));
  console.log(JSON.stringify({ passed: checks.length, checks }, null, 2));
} finally {
  if (browser) await browser.close();
  await stop(server);
  await rm(scratch, { recursive: true, force: true });
}
