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
    /** The scroll behaviour: smooth, unless the viewer has asked for less motion. */
    const behavior = () =>
        matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth';

    /**
     * Scrolls a pane to a block offset.
     *
     * @param {HTMLElement} pane The scrolling pane.
     * @param {number} wanted The offset from the pane's block start, in pixels.
     */
    const scrollPane = (pane, wanted) => {
        const before = pane.scrollTop;

        pane.scrollTo({ top: wanted, behavior: behavior() });

        // A smooth scroll can be accepted and then never performed, which loses the
        // movement the click was for rather than degrading to a jump. A timer rather
        // than requestAnimationFrame, because a hidden tab runs one and not the other.
        setTimeout(() => {
            if (pane.scrollTop === before && Math.round(wanted) !== Math.round(before)) {
                pane.scrollTo({ top: wanted, behavior: 'auto' });
            }
        }, 300);
    };

    const show = (target) => {
        if (!target) {
            return;
        }

        // A quote of an earlier wording sits in a collapsed <details>.
        // Open it first, because a closed one has no layout to scroll to.
        for (let details = target.closest('details'); details; details = details.parentElement?.closest('details')) {
            details.open = true;
        }

        const pane = target.closest('.pane');

        if (pane && pane.scrollHeight > pane.clientHeight) {
            const offset = target.getBoundingClientRect().top
                - pane.getBoundingClientRect().top
                + pane.scrollTop;

            // A third of the way down reads as "here" without pinning it to the very edge.
            scrollPane(pane, offset - pane.clientHeight / 3);
        } else {
            target.scrollIntoView({ block: 'center', behavior: behavior() });
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

    /**
     * Keeps a pane's place while live updates change its content. Without it, anything that
     * changed above the part being read would move that part.
     *
     * A pane scrolled to its start stays there, so it follows responses arriving at the top.
     * A pane scrolled further keeps its first visible item where it was. A pane holding a
     * [data-new-arrivals] button counts on it the items that arrive while the pane is scrolled
     * away from its start. Pressing the button returns the pane to its start.
     *
     * Browser scroll anchoring does the same in some browsers but not all. It would also move
     * the pane a second time after this script, so app.css turns it off on the panes that scroll.
     */
    window.whatYouSayPanes = {
        /**
         * @param {HTMLElement} pane The pane. Where the page scrolls instead of the pane, the
         *     page's own scroll anchoring keeps the place.
         * @param {string} items A selector for the items to keep in place.
         */
        keep: (pane, items) => {
            const arrivals = pane.querySelector('[data-new-arrivals]');
            let anchor = null;
            let anchorOffset = 0;
            let unseen = 0;

            // Scroll anchoring is off, so a change to the pane's content leaves scrollTop as it
            // was. A mutation callback therefore reads it as it stood before the change.
            const isAtStart = () => pane.scrollTop < 2;

            // An element's offset from the start of the pane's content. Measured from the
            // content rather than the visible edge, so a remembered anchor stays valid after a
            // scroll whose event has not arrived yet. A hidden tab delivers no scroll events.
            const offsetOf = (element) =>
                element.getBoundingClientRect().top - pane.getBoundingClientRect().top + pane.scrollTop;

            // Takes the first item that starts inside the visible part as the anchor. An item
            // starting above the visible part is no use, because something nested in it can
            // change without moving the item's start.
            const remember = () => {
                anchor = [...pane.querySelectorAll(items)].find(item => offsetOf(item) >= pane.scrollTop - 1) ?? null;
                anchorOffset = anchor ? offsetOf(anchor) : 0;
            };

            const count = () => {
                if (!arrivals) {
                    return;
                }

                const text = `${unseen} new ${unseen === 1 ? 'response' : 'responses'}`;

                // Writing even the same text is a mutation, which would call the observer again
                // without end.
                if (arrivals.textContent !== text) {
                    arrivals.textContent = text;
                }

                arrivals.hidden = unseen === 0;
            };

            pane.addEventListener('scroll', () => {
                remember();

                if (isAtStart() && unseen > 0) {
                    unseen = 0;
                    count();
                }
            }, { passive: true });

            arrivals?.addEventListener('click', () => scrollPane(pane, 0));

            new MutationObserver((records) => {
                // A pane at its start stays there unaided.
                if (!isAtStart()) {
                    if (arrivals) {
                        unseen += records
                            .flatMap(record => [...record.addedNodes])
                            .filter(node => node instanceof Element && node.matches(items))
                            .length;
                        count();
                    }

                    const moved = anchor?.isConnected ? offsetOf(anchor) - anchorOffset : 0;

                    // Assigning scrollTop stops a smooth scroll in progress, even when the value
                    // is unchanged.
                    if (moved !== 0) {
                        pane.scrollTop += moved;
                    }
                }

                remember();
            }).observe(pane, { childList: true, subtree: true, characterData: true });

            remember();
        },
    };
})();

/**
 * Select text in the editor's response pane and offer to quote it onto the picked node.
 *
 * The selected string is sent as it reads, because the pane renders each body as plain text
 * and existing quotes as <mark> around it — neither adds a character — so what comes back
 * from the selection is a substring of what is stored. The server checks anyway.
 */
window.whatYouSayQuoting = {
    attach: (editor) => {
        if (window.whatYouSayQuoting.button) {
            window.whatYouSayQuoting.editor = editor;

            return;
        }

        window.whatYouSayQuoting.editor = editor;

        const button = document.createElement('button');

        button.type = 'button';
        button.className = 'primary quote-selection';
        button.textContent = 'Quote this';
        button.hidden = true;
        document.body.append(button);
        window.whatYouSayQuoting.button = button;

        let pending = null;

        const hide = () => {
            pending = null;
            button.hidden = true;
        };

        // mouseup rather than selectionchange: the latter fires per character while dragging,
        // and the button would chase the cursor across the pane.
        document.addEventListener('mouseup', () => {
            const selection = window.getSelection();

            if (!selection || selection.isCollapsed) {
                hide();

                return;
            }

            const body = selection.anchorNode?.parentElement?.closest('[data-response]');

            // Both ends inside one body, or the string spans things that are not the response.
            if (!body || body !== selection.focusNode?.parentElement?.closest('[data-response]')) {
                hide();

                return;
            }

            const quote = selection.toString();

            if (quote.trim().length === 0) {
                hide();

                return;
            }

            pending = { response: body.dataset.response, quote };

            const box = selection.getRangeAt(0).getBoundingClientRect();

            button.hidden = false;
            button.style.top = `${box.bottom + window.scrollY + 6}px`;
            button.style.left = `${Math.max(8, box.left + window.scrollX)}px`;
        });

        button.addEventListener('click', async () => {
            if (!pending) {
                return;
            }

            const { response, quote } = pending;

            hide();
            window.getSelection()?.removeAllRanges();

            await window.whatYouSayQuoting.editor.invokeMethodAsync(
                'QuoteSelectionAsync', response, quote);
        });

        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                hide();
            }
        });
    },
};
