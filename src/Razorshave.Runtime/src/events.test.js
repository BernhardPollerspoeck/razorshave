import { describe, it, expect } from 'vitest';
import {
  MouseEventArgs,
  KeyboardEventArgs,
  ChangeEventArgs,
  EventArgs,
} from './events.js';

describe('MouseEventArgs', () => {
  it('exposes native mouse event fields with PascalCase getters', () => {
    const native = { clientX: 10, clientY: 20, button: 1, buttons: 1, ctrlKey: true };
    const args = new MouseEventArgs(native);
    expect(args.ClientX).toBe(10);
    expect(args.ClientY).toBe(20);
    expect(args.Button).toBe(1);
    expect(args.Buttons).toBe(1);
    expect(args.CtrlKey).toBe(true);
    expect(args.ShiftKey).toBe(undefined);
  });
});

describe('KeyboardEventArgs', () => {
  it('exposes key, code and modifier flags', () => {
    const native = { key: 'Enter', code: 'Enter', altKey: true, repeat: false };
    const args = new KeyboardEventArgs(native);
    expect(args.Key).toBe('Enter');
    expect(args.Code).toBe('Enter');
    expect(args.AltKey).toBe(true);
    expect(args.Repeat).toBe(false);
  });

  it('exposes Location (numeric: 0=standard, 1=left, 2=right, 3=numpad)', () => {
    // Blazor's KeyboardEventArgs.Location is the DOM event's `location`
    // verbatim — differentiates left-shift from right-shift, numpad keys,
    // etc. Was missing before Q-Batch 18.
    const leftShift = new KeyboardEventArgs({ key: 'Shift', location: 1 });
    const numpadPlus = new KeyboardEventArgs({ key: '+', location: 3 });
    expect(leftShift.Location).toBe(1);
    expect(numpadPlus.Location).toBe(3);
  });

  it('exposes IsComposing for IME-aware input handling', () => {
    const composing = new KeyboardEventArgs({ key: 'Process', isComposing: true });
    expect(composing.IsComposing).toBe(true);
  });
});

describe('ChangeEventArgs', () => {
  it('reads Value from the event target', () => {
    const args = new ChangeEventArgs({ target: { value: 'typed' } });
    expect(args.Value).toBe('typed');
  });
});

describe('EventArgs base', () => {
  it('exposes the event type', () => {
    const args = new EventArgs({ type: 'click' });
    expect(args.Type).toBe('click');
  });
});
