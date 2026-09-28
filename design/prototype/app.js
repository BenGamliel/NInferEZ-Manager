const pages = [...document.querySelectorAll('.page')];
const navItems = [...document.querySelectorAll('.nav-item')];
const pageLabel = document.querySelector('#page-label');
const toast = document.querySelector('.toast');

function openPage(pageName) {
  pages.forEach(page => page.classList.toggle('active', page.id === `page-${pageName}`));
  navItems.forEach(item => {
    const selected = item.dataset.page === pageName;
    item.classList.toggle('active', selected);
    if (selected) item.setAttribute('aria-current', 'page');
    else item.removeAttribute('aria-current');
  });
  const selected = navItems.find(item => item.dataset.page === pageName);
  pageLabel.textContent = selected?.innerText.replace(/\d+/g, '').trim() || pageName;
  window.scrollTo({ top: 0, behavior: 'instant' });
}

navItems.forEach(item => item.addEventListener('click', () => openPage(item.dataset.page)));
document.querySelectorAll('[data-page-link]').forEach(item => item.addEventListener('click', () => openPage(item.dataset.pageLink)));

document.querySelectorAll('[data-action="copy"]').forEach(button => button.addEventListener('click', async () => {
  try { await navigator.clipboard.writeText('http://127.0.0.1:8173/v1'); } catch { /* prototype can run from file:// */ }
  toast.classList.add('show');
  window.clearTimeout(window.toastTimer);
  window.toastTimer = window.setTimeout(() => toast.classList.remove('show'), 2400);
}));

document.querySelectorAll('[data-action="load"]').forEach(button => button.addEventListener('click', () => {
  const original = button.textContent;
  button.disabled = true;
  button.textContent = 'Loading model…';
  window.setTimeout(() => { button.textContent = 'Model loaded'; }, 850);
  window.setTimeout(() => { button.disabled = false; button.textContent = original; }, 2300);
}));

document.querySelectorAll('.filter').forEach(filter => filter.addEventListener('click', () => {
  if (filter.classList.contains('icon')) return;
  filter.parentElement.querySelectorAll('.filter').forEach(item => item.classList.remove('active'));
  filter.classList.add('active');
}));

document.querySelectorAll('.segmented button').forEach(button => button.addEventListener('click', () => {
  button.parentElement.querySelectorAll('button').forEach(item => item.classList.remove('active'));
  button.classList.add('active');
}));
