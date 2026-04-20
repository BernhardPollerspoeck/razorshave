// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { navigationManager } from './navigation-manager.js';

describe('NavigationManager', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/');
  });

  it('exposes the current uri and pathname', () => {
    window.history.replaceState(null, '', '/counter');
    expect(navigationManager.pathname).toBe('/counter');
  });

  it('navigateTo() pushes history and notifies subscribers', () => {
    const listener = vi.fn();
    const unsub = navigationManager.onLocationChanged(listener);

    navigationManager.navigateTo('/weather');

    expect(window.location.pathname).toBe('/weather');
    expect(listener).toHaveBeenCalledOnce();
    expect(listener).toHaveBeenCalledWith('/weather');

    unsub();
  });

  it('unsubscribing stops further notifications', () => {
    const listener = vi.fn();
    const unsub = navigationManager.onLocationChanged(listener);
    unsub();
    navigationManager.navigateTo('/other');
    expect(listener).not.toHaveBeenCalled();
  });

  it('supports multiple concurrent subscribers', () => {
    const a = vi.fn();
    const b = vi.fn();
    const unsubA = navigationManager.onLocationChanged(a);
    const unsubB = navigationManager.onLocationChanged(b);

    navigationManager.navigateTo('/x');

    expect(a).toHaveBeenCalledWith('/x');
    expect(b).toHaveBeenCalledWith('/x');
    unsubA(); unsubB();
  });

  it('listener that unsubscribes during dispatch does not break iteration', () => {
    // Regression for the live-Set iteration bug: _emit used to iterate the
    // listener Set directly, so a listener calling its own unsub (or
    // onLocationChanged to add another) during dispatch would corrupt the
    // walk and silently skip or re-fire listeners.
    const offs = [];
    const a = vi.fn(() => offs[0]());
    const b = vi.fn();
    offs.push(navigationManager.onLocationChanged(a));
    const unsubB = navigationManager.onLocationChanged(b);

    expect(() => navigationManager.navigateTo('/x')).not.toThrow();
    expect(a).toHaveBeenCalledOnce();
    expect(b).toHaveBeenCalledOnce();
    unsubB();
  });
});
