// Blazor-style EventArgs wrappers.
//
// The transpiler emits `new MouseEventArgs(e)` inside event-handler arrow
// functions, so every EventArgs class takes the native DOM event as its sole
// constructor argument and exposes its fields with Blazor's PascalCase names.
//
// Only the EventArgs variants exercised by the fixture tests and the Counter /
// Weather / MainLayout demos are implemented here. Others land as they are
// needed — the shape is trivial, growing the list is mechanical.

export class EventArgs {
  constructor(native) {
    this._native = native;
  }

  get Type() { return this._native?.type; }
}

export class MouseEventArgs extends EventArgs {
  get ClientX()  { return this._native.clientX; }
  get ClientY()  { return this._native.clientY; }
  get ScreenX()  { return this._native.screenX; }
  get ScreenY()  { return this._native.screenY; }
  get OffsetX()  { return this._native.offsetX; }
  get OffsetY()  { return this._native.offsetY; }
  get Button()   { return this._native.button; }
  get Buttons()  { return this._native.buttons; }
  get CtrlKey()  { return this._native.ctrlKey; }
  get ShiftKey() { return this._native.shiftKey; }
  get AltKey()   { return this._native.altKey; }
  get MetaKey()  { return this._native.metaKey; }
  get Detail()   { return this._native.detail; }
}

export class KeyboardEventArgs extends EventArgs {
  get Key()      { return this._native.key; }
  get Code()     { return this._native.code; }
  get CtrlKey()  { return this._native.ctrlKey; }
  get ShiftKey() { return this._native.shiftKey; }
  get AltKey()   { return this._native.altKey; }
  get MetaKey()  { return this._native.metaKey; }
  get Repeat()   { return this._native.repeat; }
  // Location maps the numeric DOM value (0 = standard, 1 = left, 2 = right,
  // 3 = numpad) directly onto Blazor's identically-named property.
  get Location() { return this._native.location; }
  // Blazor's KeyboardEventArgs carries `IsComposing` — surface it so form
  // components with IME compose semantics can check it without reaching
  // into `_native`.
  get IsComposing() { return this._native.isComposing; }
}

export class ChangeEventArgs extends EventArgs {
  get Value() { return this._native.target?.value; }
}

export class FocusEventArgs extends EventArgs {
  // Blazor's FocusEventArgs has no extra properties beyond the base.
}
