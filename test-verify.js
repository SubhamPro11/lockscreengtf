const fs = require('fs');
const assert = require('assert');

// 1. Verify index.html content & targets
const html = fs.readFileSync('index.html', 'utf8');

// Required features & sync points
assert(html.includes('WinUI 3'), 'index.html missing WinUI 3 reference');
assert(html.includes('CommunityToolkit.Mvvm'), 'index.html missing CommunityToolkit.Mvvm reference');
assert(html.includes('cmd-build'), 'index.html missing cmd-build id');
assert(html.includes('Remove Animated Lockscreen'), 'index.html missing in-app removal instruction');
assert(html.includes('Diagnostics panel'), 'index.html missing diagnostics workflow');
assert(html.includes('btn-copy-build'), 'index.html missing build copy button');

// Nav anchor targets exist
const navTargets = ['how-it-works', 'requirements', 'troubleshooting', 'opensource'];
navTargets.forEach(target => {
  assert(html.includes(`id="${target}"`), `Target id="${target}" not found in index.html`);
});

// Copy button targets exist
const copyTargetMatches = [...html.matchAll(/data-copy-target="([^"]+)"/g)];
assert(copyTargetMatches.length > 0, 'No data-copy-target found');
copyTargetMatches.forEach(m => {
  const targetId = m[1];
  assert(html.includes(`id="${targetId}"`), `Copy target id="${targetId}" not found in index.html`);
});

// 2. Verify style.css syntax (balanced braces)
const css = fs.readFileSync('css/style.css', 'utf8');
const openBraces = (css.match(/\{/g) || []).length;
const closeBraces = (css.match(/\}/g) || []).length;
assert.strictEqual(openBraces, closeBraces, `Unbalanced braces in style.css: {=${openBraces}, }=${closeBraces}`);
assert(css.includes('.terminal-code.is-multiline'), 'style.css missing multiline terminal style');

// 3. Verify README.md sync
const readme = fs.readFileSync('README.md', 'utf8');
assert(readme.includes('WinUI 3'), 'README.md missing WinUI 3');
assert(readme.includes('Diagnostics'), 'README.md missing Diagnostics');
assert(readme.includes('CommunityToolkit.Mvvm'), 'README.md missing CommunityToolkit.Mvvm');

// 4. Verify local assets
assert(fs.existsSync('assets/overhead-desk-workstation.jpg'), 'Missing overhead-desk-workstation.jpg asset');

console.log('ALL CHECKS PASSED!');
