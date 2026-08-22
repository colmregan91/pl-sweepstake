// Lets the page stop polling while the tab is in the background, and catch up the moment it
// comes back. One page, one subscription -- no registry needed.

let handler = null;

export function subscribe(dotNetRef) {
    unsubscribe();
    handler = () => dotNetRef.invokeMethodAsync('OnVisibilityChanged', !document.hidden);
    document.addEventListener('visibilitychange', handler);
    return !document.hidden;
}

export function unsubscribe() {
    if (handler !== null) {
        document.removeEventListener('visibilitychange', handler);
        handler = null;
    }
}
