/**
 * Which assessment this web app belongs to (backlog id 99). #7 and #8 share a
 * page structure and both default to port 3010, so every place a user looks —
 * the signed-in header, the sign-in page and the browser tab — names the stack.
 *
 * This is the ONLY definition: the header, the login page and the tab title all
 * read it from here, and src/app/stack-label.test.tsx fails if the text is
 * written anywhere else in the app's source.
 */
export const STACK_LABEL = '#8 · .NET / Next.js';

/** The browser-tab title. */
export const APP_TITLE = `Order-To-Cash · ${STACK_LABEL}`;
