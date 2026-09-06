/**
 * Copies a readonly field the page only ever shows once, so the value never has to be
 * selected by hand off a page that will not show it again.
 *
 * @param {HTMLTextAreaElement} source The field to copy from.
 * @param {HTMLElement} button The control that was clicked, relabelled to confirm.
 * @returns {Promise<void>}
 */
window.whatYouSayCopy = async (source, button) => {
    try {
        await navigator.clipboard.writeText(source.value);
    } catch {
        // Needs a secure context, which plain http on a LAN is not. Select it instead so
        // the keyboard still works.
        source.select();

        return;
    }

    const originalButtonTextContent = button.textContent;

    button.textContent = 'Copied';
    setTimeout(() => button.textContent = originalButtonTextContent, 2000);
};

/**
 * Moves one pane of the summary split to show something the other pane points at, in either
 * direction: a citation in the tree shows the quote it was taken from, and a quote shows the
 * note it supports.
 *
 * Deliberately not an anchor. An in-page href scrolls every scrollable ancestor, so the pane
 * moved and the whole page lurched with it. Setting scrollTop on the pane alone moves only
 * what has to move; where the columns have stacked there is no scrolling pane and the page
 * scroll is the right one to use.
 */
(() => {
    const show = (target) => {
        if (!target) {
            return;
        }

        const pane = target.closest('.pane');

        if (pane && pane.scrollHeight > pane.clientHeight) {
            const offset = target.getBoundingClientRect().top
                - pane.getBoundingClientRect().top
                + pane.scrollTop;

            // A third of the way down reads as "here" without pinning it to the very edge.
            const wanted = offset - pane.clientHeight / 3;
            const before = pane.scrollTop;

            pane.scrollTo({ top: wanted, behavior: 'smooth' });

            // A smooth scroll can be accepted and then never performed, which loses the
            // movement the click was for rather than degrading to a jump. A timer rather
            // than requestAnimationFrame, because a hidden tab runs one and not the other.
            setTimeout(() => {
                if (pane.scrollTop === before && Math.round(wanted) !== Math.round(before)) {
                    pane.scrollTo({ top: wanted, behavior: 'auto' });
                }
            }, 300);
        } else {
            target.scrollIntoView({ block: 'center', behavior: 'smooth' });
        }

        for (const previous of document.querySelectorAll('.is-hit')) {
            previous.classList.remove('is-hit');
        }

        target.classList.add('is-hit');
    };

    document.addEventListener('click', (event) => {
        const opener = event.target.closest('[data-opens]');

        if (opener) {
            event.preventDefault();

            const panel = document.getElementById(opener.dataset.opens);

            if (panel) {
                panel.open = true;
                panel.querySelector('textarea')?.focus();
            }

            return;
        }

        const source = event.target.closest('[data-shows-quote], [data-shows-node]');

        if (!source) {
            return;
        }

        // Both ends are inside the reaction form, so a stray submit would reload the page.
        event.preventDefault();

        // A quote is found by reference id rather than by element id, because one run of
        // text can answer to several references.
        show(source.dataset.showsQuote
            ? document.querySelector(`mark[data-refs~="${source.dataset.showsQuote}"]`)
            : document.getElementById(source.dataset.showsNode));
    });
})();
