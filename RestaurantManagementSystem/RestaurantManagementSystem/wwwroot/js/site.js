// Global site scripts
// Enhancements: Keyboard navigation + scrollable long dropdown menus in the top navigation.

(() => {
	function initScrollableDropdowns() {
		document.querySelectorAll('.navbar-nav .dropdown-menu').forEach(menu => {
			const items = menu.querySelectorAll('.dropdown-item');
			if (items.length > 10) {
				menu.classList.add('scrollable-nav-dropdown');
			}
			// Ensure each item can be focused programmatically
			items.forEach(a => {
				if (!a.hasAttribute('tabindex')) {
					a.setAttribute('tabindex', '-1');
				}
			});
		});
	}

	function handleKeyboardNavigation(e) {
		const menu = e.currentTarget;
		const items = Array.from(menu.querySelectorAll('.dropdown-item'));
		if (!items.length) return;
		const activeEl = document.activeElement;
		const currentIndex = items.indexOf(activeEl);

		if (e.key === 'ArrowDown') {
			e.preventDefault();
			let nextIndex = currentIndex + 1;
			if (nextIndex >= items.length) nextIndex = 0;
			items[nextIndex].focus();
		} else if (e.key === 'ArrowUp') {
			e.preventDefault();
			let prevIndex = currentIndex - 1;
			if (prevIndex < 0) prevIndex = items.length - 1;
			items[prevIndex].focus();
		} else if (e.key === 'Home') {
			e.preventDefault();
			items[0].focus();
		} else if (e.key === 'End') {
			e.preventDefault();
			items[items.length - 1].focus();
		}
	}

	function focusFirstItemOnShow(ev) {
		const toggle = ev.target; // element that triggered show
		const menu = toggle.parentElement?.querySelector('.dropdown-menu');
		if (!menu) return;
		const first = menu.querySelector('.dropdown-item');
		if (first) {
			// Delay to ensure menu is visible
			setTimeout(() => first.focus(), 50);
		}
	}

	function attachEvents() {
		// Bootstrap event when dropdown shown
		document.addEventListener('shown.bs.dropdown', focusFirstItemOnShow);
		// Delegate keyboard handling
		document.querySelectorAll('.navbar-nav .dropdown-menu').forEach(menu => {
			menu.addEventListener('keydown', handleKeyboardNavigation);
		});
	}

	// Inject minimal CSS for scroll behavior (only once)
	function injectScrollStyles() {
		if (document.getElementById('nav-dropdown-scroll-styles')) return;
		const style = document.createElement('style');
		style.id = 'nav-dropdown-scroll-styles';
		style.textContent = `.scrollable-nav-dropdown{max-height:420px;overflow-y:auto;overscroll-behavior:contain;}
		.scrollable-nav-dropdown::-webkit-scrollbar{width:8px}
		.scrollable-nav-dropdown::-webkit-scrollbar-track{background:#f1f1f1;border-radius:8px}
		.scrollable-nav-dropdown::-webkit-scrollbar-thumb{background:#c7d2fe;border-radius:8px}
		.scrollable-nav-dropdown::-webkit-scrollbar-thumb:hover{background:#667eea}`;
		document.head.appendChild(style);
	}

	document.addEventListener('DOMContentLoaded', () => {
		injectScrollStyles();
		initScrollableDropdowns();
		attachEvents();
	});
})();

