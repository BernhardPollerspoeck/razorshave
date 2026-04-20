// Reactive key/value store — one Store per Razor generic `IStore<T>`. A
// component injects `IStore<User> Users`, mutates via set/delete/clear, and
// subscribes via onChange() to be notified when anything in the store
// changes. The container auto-creates a singleton Store for every unique
// `IStore<…>` key it sees, so apps get shared state for free without any
// registration boilerplate.
//
// Mirrors the IStore<T> surface specified in RAZORSHAVE-RUNTIME-API.md.
// Notifications are coarse — any mutation fires every listener once — which
// is intentional for the M0/v0.1 scope and matches Blazor StateHasChanged
// semantics.

export class Store {
  constructor() {
    this._data = new Map();
    this._listeners = new Set();
    this._batchDepth = 0;
    this._batchDirty = false;
  }

  get(key) { return this._data.get(key); }

  set(key, value) {
    this._data.set(key, value);
    this._notifyChange();
  }

  delete(key) {
    const existed = this._data.delete(key);
    if (existed) this._notifyChange();
    return existed;
  }

  has(key) { return this._data.has(key); }

  getAll() { return Array.from(this._data.values()); }

  where(predicate) {
    return Array.from(this._data.values()).filter(predicate);
  }

  clear() {
    if (this._data.size === 0) return;
    this._data.clear();
    this._notifyChange();
  }

  get count() { return this._data.size; }

  // Runs `updates` with change notifications suppressed and emits a single
  // change at the end if anything actually mutated. Supports nesting — only
  // the outermost batch flushes.
  //
  // If `updates` throws, we drop the pending dirty flag without emitting —
  // partially-mutated state would otherwise trigger a UI update against a
  // half-applied change. The exception still propagates so the caller can
  // recover; the error boundary just doesn't paint an incoherent frame.
  batch(updates) {
    this._batchDepth++;
    let threw = false;
    try {
      updates();
    } catch (e) {
      threw = true;
      throw e;
    } finally {
      this._batchDepth--;
      if (this._batchDepth === 0) {
        const shouldEmit = this._batchDirty && !threw;
        this._batchDirty = false;
        if (shouldEmit) this._emitChange();
      }
    }
  }

  // Subscribe to change notifications. Returns an unsubscribe function so the
  // usual `onInit → subscribe, onDestroy → unsubscribe` dance is one line.
  // For transpiled Razor components the returned fn is unused — the runtime
  // calls `offChange(handler)` instead with the same bound reference.
  //
  // Listener set is snapshotted before dispatch (see `_emitChange`): a
  // listener that subscribes DURING dispatch does not see the current event,
  // only subsequent ones. A listener that unsubscribes itself during dispatch
  // is safely removed for future events.
  onChange(handler) {
    this._listeners.add(handler);
    return () => this._listeners.delete(handler);
  }

  // Symmetric counterpart for C# `event -=` transpilation. Passing the same
  // function reference used in onChange() removes it; unknown refs are a no-op.
  offChange(handler) {
    this._listeners.delete(handler);
  }

  _notifyChange() {
    if (this._batchDepth > 0) {
      this._batchDirty = true;
      return;
    }
    this._emitChange();
  }

  _emitChange() {
    // A listener is allowed to mutate the store (common pattern: derived
    // values). Unbounded recursion, however, turns into a silent stack-
    // exhaustion crash — guard with a small depth counter that throws with
    // a named diagnostic once the chain exceeds a sane limit.
    this._emitDepth = (this._emitDepth ?? 0) + 1;
    try {
      if (this._emitDepth > 8) {
        throw new Error(
          '[razorshave] Store onChange recursion exceeded 8 levels — a listener '
          + 'is mutating the store inside its own notification chain. Guard the mutation '
          + 'behind an equality check or move it into Store.batch().'
        );
      }
      // Snapshot the listener set — handlers may unsubscribe during dispatch.
      for (const listener of Array.from(this._listeners)) {
        listener();
      }
    } finally {
      this._emitDepth--;
    }
  }
}
