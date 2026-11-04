document.addEventListener('DOMContentLoaded', () => {
  initStickyHeader();
  initMobileNav();
  initActiveNavObserver();
  initCopyCommand();
  fetchLiveReleaseData();
});

function initStickyHeader() {
  const header = document.querySelector('.site-header');
  if (!header) return;
  const onScroll = () => header.classList.toggle('is-scrolled', window.scrollY > 15);
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
}

function initMobileNav() {
  const header = document.querySelector('.site-header');
  const toggleBtn = document.getElementById('nav-toggle');
  const navList = document.getElementById('primary-nav');
  if (!toggleBtn || !header) return;

  const toggleText = toggleBtn.querySelector('.toggle-text');

  toggleBtn.addEventListener('click', () => {
    const isOpen = header.classList.toggle('is-menu-open');
    toggleBtn.setAttribute('aria-expanded', String(isOpen));
    if (toggleText) toggleText.textContent = isOpen ? '[close]' : '[menu]';
  });

  if (navList) {
    navList.querySelectorAll('a').forEach(link => {
      link.addEventListener('click', () => {
        header.classList.remove('is-menu-open');
        toggleBtn.setAttribute('aria-expanded', 'false');
        if (toggleText) toggleText.textContent = '[menu]';
      });
    });
  }
}

function initActiveNavObserver() {
  const sections = document.querySelectorAll('main section[id]');
  const navLinks = document.querySelectorAll('.nav-links .nav-link');
  if (!sections.length || !navLinks.length) return;

  const observer = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        const id = entry.target.getAttribute('id');
        navLinks.forEach(link => {
          const href = link.getAttribute('href');
          link.classList.toggle('is-active', href === `#${id}`);
        });
      }
    });
  }, { rootMargin: '-20% 0px -60% 0px' });

  sections.forEach(sec => observer.observe(sec));
}

function fallbackCopy(text) {
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.style.cssText = 'position:fixed;opacity:0';
  document.body.appendChild(ta);
  ta.select();
  document.execCommand('copy');
  ta.remove();
  return Promise.resolve();
}

function copyToClipboard(text) {
  if (navigator.clipboard?.writeText && window.isSecureContext) {
    return navigator.clipboard.writeText(text).catch(() => fallbackCopy(text));
  }
  return fallbackCopy(text);
}

function initCopyCommand() {
  const buttons = document.querySelectorAll('.btn-copy-code');
  buttons.forEach(btn => {
    const targetId = btn.getAttribute('data-copy-target') || 'cmd-run';
    const code = document.getElementById(targetId);
    if (!code) return;

    const copyIcon = btn.querySelector('.copy-icon');
    const checkIcon = btn.querySelector('.check-icon');
    const defaultLabel = btn.getAttribute('aria-label') || 'Copy command';

    btn.addEventListener('click', async () => {
      const textToCopy = code.textContent.trim();
      try {
        await copyToClipboard(textToCopy);
      } catch (_) {}
      if (copyIcon) copyIcon.style.display = 'none';
      if (checkIcon) checkIcon.style.display = 'inline-block';
      btn.classList.add('is-copied');
      btn.setAttribute('aria-label', 'Command copied to clipboard');
      setTimeout(() => {
        if (copyIcon) copyIcon.style.display = 'inline-block';
        if (checkIcon) checkIcon.style.display = 'none';
        btn.classList.remove('is-copied');
        btn.setAttribute('aria-label', defaultLabel);
      }, 2000);
    });
  });
}

function isSafeReleaseUrl(url) {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' && 
      (parsed.hostname === 'github.com' || parsed.hostname.endsWith('.github.com') || parsed.hostname === 'objects.githubusercontent.com');
  } catch {
    return false;
  }
}

async function fetchLiveReleaseData() {
  try {
    const res = await fetch('https://api.github.com/repos/SubhamPro11/lockscreengtf/releases/latest');
    if (!res.ok) return;
    const release = await res.json();
    if (!release?.assets) return;

    const msi = release.assets.find(a => a.name.endsWith('.msi'))?.browser_download_url;
    if (msi && isSafeReleaseUrl(msi)) {
      ['hero-btn-download', 'nav-download-btn', 'req-btn-msi'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.href = msi;
      });
    }
    const zip = release.assets.find(a => a.name.endsWith('.zip'))?.browser_download_url;
    if (zip && isSafeReleaseUrl(zip)) {
      ['hero-btn-zip', 'req-btn-zip'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.href = zip;
      });
    }
  } catch (_) {}
}
