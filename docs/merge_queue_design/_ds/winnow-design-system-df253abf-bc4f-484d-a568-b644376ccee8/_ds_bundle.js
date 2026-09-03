/* @ds-bundle: {"format":4,"namespace":"WinnowDesignSystem_df253a","components":[{"name":"Badge","sourcePath":"components/core/Badge.jsx"},{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Checkbox","sourcePath":"components/core/Checkbox.jsx"},{"name":"CountPill","sourcePath":"components/core/CountPill.jsx"},{"name":"DensitySlider","sourcePath":"components/core/DensitySlider.jsx"},{"name":"TextField","sourcePath":"components/core/TextField.jsx"},{"name":"UnreadDot","sourcePath":"components/core/UnreadDot.jsx"},{"name":"CutChip","sourcePath":"components/feedback/CutChip.jsx"},{"name":"DockCard","sourcePath":"components/feedback/DockCard.jsx"},{"name":"EmptyState","sourcePath":"components/feedback/EmptyState.jsx"},{"name":"RatingDots","sourcePath":"components/feedback/RatingDots.jsx"},{"name":"StatusPip","sourcePath":"components/feedback/StatusPip.jsx"},{"name":"FeedCard","sourcePath":"components/library/FeedCard.jsx"},{"name":"GameTile","sourcePath":"components/library/GameTile.jsx"},{"name":"GapRail","sourcePath":"components/library/GapRail.jsx"},{"name":"LibraryRow","sourcePath":"components/library/LibraryRow.jsx"},{"name":"SectionPanel","sourcePath":"components/library/SectionPanel.jsx"},{"name":"RailRow","sourcePath":"components/navigation/RailRow.jsx"},{"name":"SegmentedToggle","sourcePath":"components/navigation/SegmentedToggle.jsx"},{"name":"SortMenu","sourcePath":"components/navigation/SortMenu.jsx"},{"name":"TitleBar","sourcePath":"components/navigation/TitleBar.jsx"}],"sourceHashes":{"components/core/Badge.jsx":"675cee76d744","components/core/Button.jsx":"0e5a720b5c6f","components/core/Checkbox.jsx":"32b9d502d7c1","components/core/CountPill.jsx":"c411ec878d11","components/core/DensitySlider.jsx":"4251e94c7f8c","components/core/TextField.jsx":"47216df94568","components/core/UnreadDot.jsx":"ae14c31c7568","components/feedback/CutChip.jsx":"25846d5b3a73","components/feedback/DockCard.jsx":"e32e95fef93a","components/feedback/EmptyState.jsx":"09ee7b95a15e","components/feedback/RatingDots.jsx":"8cc122bb7735","components/feedback/StatusPip.jsx":"d8da49662b34","components/library/FeedCard.jsx":"ae6eb8b280e9","components/library/GameTile.jsx":"18db4d70aaa9","components/library/GapRail.jsx":"971737d9a94a","components/library/LibraryRow.jsx":"f26ad25c280e","components/library/SectionPanel.jsx":"4efbc9931fb0","components/navigation/RailRow.jsx":"7d192f2314df","components/navigation/SegmentedToggle.jsx":"c981ebd4db08","components/navigation/SortMenu.jsx":"4310033b2581","components/navigation/TitleBar.jsx":"3cea179126ff","ui_kits/desktop-app/app.jsx":"5dd98e34b403","ui_kits/desktop-app/data.js":"9298a2f9fc99","ui_kits/desktop-app/details.jsx":"f22af882da88","ui_kits/desktop-app/feed.jsx":"b4a42635a4f8","ui_kits/desktop-app/filters.jsx":"bd150fdafdf4","ui_kits/desktop-app/library.jsx":"0a440c08e52b","ui_kits/desktop-app/shell.jsx":"4f3d9bfca44e"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.WinnowDesignSystem_df253a = window.WinnowDesignSystem_df253a || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/core/Badge.jsx
try { (() => {
const React = window.React;

/* Two badges, and which one you use is a grammar rule rather than a style
   choice. A NAME (a store, a bucket, an install state) wears a badge; a
   STATE the interface is asserting wears a colour. Outline is for the thing
   the sentence never states — the store. Fill is for a bucket or install
   chip sitting on a card. */
function Badge(props) {
  const outline = (props.variant || 'outline') === 'outline';
  const style = outline ? {
    fontFamily: 'var(--type-ui)',
    fontSize: '9px',
    letterSpacing: 'var(--track-badge)',
    color: 'var(--text-dim)',
    border: '1px solid var(--line)',
    borderRadius: 'var(--radius-badge)',
    padding: '1px 5px'
  } : {
    fontFamily: 'var(--type-ui)',
    fontSize: '11px',
    color: 'var(--text-dim)',
    background: 'var(--surface-raised)',
    borderRadius: 'var(--radius-badge)',
    padding: '3px 7px'
  };
  return React.createElement('span', {
    style: Object.assign({
      display: 'inline-block',
      whiteSpace: 'nowrap'
    }, style, props.style)
  }, props.children);
}
Object.assign(__ds_scope, { Badge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Badge.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
const React = window.React;
const PAD = {
  md: '8px 16px',
  sm: '6px 14px',
  xs: '5px 8px'
};
const SIZE = {
  md: 13,
  sm: 12,
  xs: 11
};

/* The app's action set. Primary is the one filled treatment in the product —
   the Play button, "Same game", Save. Quiet is the other half of a choice.
   Link is outbound (Azure). Secondary acts on Winnow's own data (Text ink).
   Ctl is a command-bar control: no border, lit when its panel is open. */
function Button(props) {
  const variant = props.variant || 'primary';
  const size = props.size || 'md';
  const [hover, setHover] = React.useState(false);
  const [press, setPress] = React.useState(false);
  const [focus, setFocus] = React.useState(false);
  const on = !!props.active;
  const base = {
    font: 'inherit',
    fontFamily: 'var(--type-ui)',
    fontWeight: 600,
    fontSize: SIZE[size] + 'px',
    lineHeight: '1.25',
    padding: PAD[size],
    borderRadius: 'var(--radius-control)',
    borderStyle: 'solid',
    borderWidth: '2px',
    cursor: props.disabled ? 'default' : 'pointer',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '7px',
    whiteSpace: 'nowrap',
    opacity: props.disabled ? 0.4 : 1,
    outline: 'none',
    transition: 'background var(--dur-caption) linear, border-color var(--dur-caption) linear'
  };
  const skins = {
    primary: {
      background: press ? 'var(--volt-press)' : hover ? 'var(--volt-hover)' : 'var(--volt)',
      borderColor: focus ? 'var(--volt-ink)' : press ? 'var(--volt-press)' : hover ? 'var(--volt-hover)' : 'var(--volt)',
      color: 'var(--volt-ink)'
    },
    quiet: {
      background: hover || press ? 'var(--surface-raised)' : 'transparent',
      borderColor: focus ? 'var(--volt)' : hover ? 'var(--text-dim)' : 'var(--line)',
      color: 'var(--text)'
    },
    link: {
      background: hover || press ? 'var(--surface-raised)' : 'transparent',
      borderColor: focus ? 'var(--volt)' : 'var(--line)',
      borderWidth: '1px',
      padding: '6px 11px',
      color: 'var(--azure)'
    },
    secondary: {
      background: hover || press ? 'var(--surface-raised)' : 'transparent',
      borderColor: focus ? 'var(--volt)' : 'var(--line)',
      borderWidth: '1px',
      padding: '6px 11px',
      color: 'var(--text)'
    },
    ctl: {
      background: on || hover || press ? 'var(--chrome-raised)' : 'transparent',
      borderColor: focus ? 'var(--volt)' : 'transparent',
      borderWidth: '2px',
      padding: '5px 9px',
      fontWeight: 400,
      fontSize: '12px',
      color: on ? 'var(--text)' : 'var(--text-dim)'
    },
    aside: {
      background: hover || press ? 'var(--surface-high)' : 'transparent',
      borderColor: focus ? 'var(--volt)' : 'transparent',
      color: hover || press ? 'var(--text)' : 'var(--text-dim)'
    }
  };
  return React.createElement('button', {
    type: 'button',
    title: props.tooltip,
    disabled: props.disabled,
    onClick: props.onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => {
      setHover(false);
      setPress(false);
    },
    onMouseDown: () => setPress(true),
    onMouseUp: () => setPress(false),
    onFocus: e => {
      if (e.target.matches(':focus-visible')) setFocus(true);
    },
    onBlur: () => setFocus(false),
    style: Object.assign({}, base, skins[variant], props.style)
  }, props.icon, props.children);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/Checkbox.jsx
try { (() => {
const React = window.React;

/* Drawn rather than restyled: a 16px box, a 3px radius and a VoltInk tick.
   A checked box is Volt whoever ticked it — a tick means "in force", and
   provenance is the cut bar's job, not the panel's. */
function Checkbox(props) {
  const [hover, setHover] = React.useState(false);
  const [focus, setFocus] = React.useState(false);
  const dead = props.count === 0 && !props.checked;
  return React.createElement('label', {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: 'grid',
      gridTemplateColumns: '16px 10px 1fr auto',
      alignItems: 'center',
      cursor: dead ? 'default' : 'pointer',
      opacity: dead ? 0.4 : 1,
      minHeight: '26px'
    }
  }, React.createElement('span', {
    style: {
      position: 'relative',
      width: '16px',
      height: '16px',
      borderRadius: '3px',
      background: props.checked ? 'var(--volt)' : 'var(--surface)',
      border: '1px solid ' + (props.checked ? 'var(--volt)' : hover ? 'var(--text-dim)' : 'var(--line)'),
      boxShadow: focus ? '0 0 0 2px var(--volt)' : 'none'
    }
  }, React.createElement('input', {
    type: 'checkbox',
    checked: !!props.checked,
    disabled: dead,
    onChange: props.onChange ? e => props.onChange(e.target.checked) : undefined,
    onFocus: () => setFocus(true),
    onBlur: () => setFocus(false),
    style: {
      position: 'absolute',
      inset: 0,
      opacity: 0,
      margin: 0,
      cursor: 'inherit'
    }
  }), props.checked ? React.createElement('svg', {
    width: 9,
    height: 8,
    viewBox: '0 0 9 8',
    style: {
      position: 'absolute',
      left: '3px',
      top: '4px'
    }
  }, React.createElement('path', {
    d: 'M 0,3.4 L 2.9,6.3 L 8.2,1',
    fill: 'none',
    stroke: 'var(--volt-ink)',
    strokeWidth: 2,
    strokeLinecap: 'round',
    strokeLinejoin: 'round'
  })) : null), React.createElement('span', null), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      color: 'var(--text)'
    }
  }, props.label), props.count === undefined ? null : React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: '11px',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)',
      paddingLeft: '10px'
    }
  }, props.count));
}
Object.assign(__ds_scope, { Checkbox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Checkbox.jsx", error: String((e && e.message) || e) }); }

// components/core/CountPill.jsx
try { (() => {
const React = window.React;

/* How many rules are in force. Volt, because a filter you set is a
   selection, and selection is what Volt is for. */
function CountPill(props) {
  return React.createElement('span', {
    style: Object.assign({
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      minWidth: '17px',
      height: '16px',
      padding: '0 5px',
      borderRadius: '8px',
      background: 'var(--volt)',
      color: 'var(--volt-ink)',
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data-s)',
      fontVariantNumeric: 'tabular-nums'
    }, props.style)
  }, props.children);
}
Object.assign(__ds_scope, { CountPill });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/CountPill.jsx", error: String((e && e.message) || e) }); }

// components/core/DensitySlider.jsx
try { (() => {
const React = window.React;

/* Chrome only, so it stays quiet next to the art: a 2px rule, TextDim
   behind the thumb and Line ahead of it, and a 12px thumb that brightens
   under the pointer. Never Volt — a density setting is not a selection. */
function DensitySlider(props) {
  const min = props.min === undefined ? 108 : props.min;
  const max = props.max === undefined ? 200 : props.max;
  const value = props.value === undefined ? 148 : props.value;
  const [hover, setHover] = React.useState(false);
  const pct = (value - min) / (max - min) * 100;
  return React.createElement('span', {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: '10px'
    }
  }, props.label === undefined ? null : React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 600,
      fontSize: '10px',
      letterSpacing: 'var(--track-label)',
      textTransform: 'uppercase',
      color: 'var(--text-dim)'
    }
  }, props.label), React.createElement('span', {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      position: 'relative',
      width: '104px',
      height: '12px',
      display: 'inline-block'
    }
  }, React.createElement('span', {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      top: '5px',
      height: '2px',
      background: 'var(--line)'
    }
  }), React.createElement('span', {
    style: {
      position: 'absolute',
      left: 0,
      width: pct + '%',
      top: '5px',
      height: '2px',
      background: 'var(--text-dim)'
    }
  }), React.createElement('span', {
    style: {
      position: 'absolute',
      left: 'calc(' + pct + '% - 6px)',
      top: 0,
      width: '12px',
      height: '12px',
      borderRadius: '50%',
      background: hover ? 'var(--text)' : 'var(--text-dim)'
    }
  }), React.createElement('input', {
    type: 'range',
    min: min,
    max: max,
    value: value,
    onChange: props.onChange ? e => props.onChange(Number(e.target.value)) : undefined,
    style: {
      position: 'absolute',
      inset: 0,
      width: '100%',
      opacity: 0,
      margin: 0,
      cursor: 'pointer'
    }
  })));
}
Object.assign(__ds_scope, { DensitySlider });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/DensitySlider.jsx", error: String((e && e.message) || e) }); }

// components/core/TextField.jsx
try { (() => {
const React = window.React;

/* A field is FOUND by its border and LIT by its ring, not filled to stand
   out: one step from its container in the neutral family, in the direction
   the palette already took. A pane is Ground, so a field on it is Surface;
   the rail is Surface, so a field in it is Ground. */
function TextField(props) {
  const [focus, setFocus] = React.useState(false);
  const bg = props.on === 'surface' ? 'var(--field-on-surface)' : 'var(--field-on-ground)';
  return React.createElement('input', {
    type: 'text',
    value: props.value,
    placeholder: props.placeholder,
    onChange: props.onChange ? e => props.onChange(e.target.value) : undefined,
    onFocus: () => setFocus(true),
    onBlur: () => setFocus(false),
    style: Object.assign({
      height: '30px',
      width: props.width || '100%',
      padding: '0 10px',
      background: bg,
      color: 'var(--text)',
      caretColor: 'var(--volt)',
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      border: '1px solid ' + (focus ? 'var(--volt)' : 'var(--line)'),
      borderRadius: 'var(--radius-control)',
      outline: 'none',
      boxSizing: 'border-box'
    }, props.style)
  });
}
Object.assign(__ds_scope, { TextField });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/TextField.jsx", error: String((e && e.message) || e) }); }

// components/core/UnreadDot.jsx
try { (() => {
const React = window.React;

/* THE unread signal: a game was patched after your last session. Flare
   appears here, on the rail's one pip, and on the gap rail's marks —
   nowhere else in the product, ever. */
const SIZES = {
  tile: {
    d: 14,
    ring: 2,
    glow: 'var(--badge-glow)'
  },
  row: {
    d: 8,
    ring: 0,
    glow: 'var(--pip-glow)'
  },
  pip: {
    d: 7,
    ring: 0,
    glow: 'var(--pip-glow)'
  }
};
function UnreadDot(props) {
  const s = SIZES[props.size || 'tile'];
  return React.createElement('span', {
    title: props.tooltip || '3 updates since you played',
    style: Object.assign({
      display: 'inline-block',
      width: s.d + 'px',
      height: s.d + 'px',
      borderRadius: '50%',
      background: 'var(--flare)',
      border: s.ring ? s.ring + 'px solid var(--ground)' : 'none',
      boxShadow: s.glow,
      flex: 'none'
    }, props.style)
  });
}
Object.assign(__ds_scope, { UnreadDot });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/UnreadDot.jsx", error: String((e && e.message) || e) }); }

// components/feedback/CutChip.jsx
try { (() => {
const React = window.React;

/* The cut bar's grammar, and it is the palette's own: VOLT MEANS YOU CHOSE
   THIS. A rule an open live list contributed was not chosen by the user, so
   it drops the Volt edge and takes the neutral Line one with a TextDim
   label. Three families, two edges, and each chip says which it is in words
   as well — never colour alone. */
function CutChip(props) {
  const kind = props.kind || 'user';
  const user = kind === 'user';
  const [hover, setHover] = React.useState(false);
  return React.createElement('span', {
    title: props.tooltip,
    style: Object.assign({
      display: 'inline-flex',
      alignItems: 'center',
      gap: '8px',
      height: '26px',
      padding: '0 8px',
      border: '1px solid ' + (user ? 'var(--volt)' : 'var(--line)'),
      borderRadius: 'var(--radius-control)',
      background: 'var(--surface-raised)',
      fontFamily: 'var(--type-ui)',
      fontSize: '12px',
      color: user ? 'var(--text)' : 'var(--text-dim)',
      whiteSpace: 'nowrap'
    }, props.style)
  }, kind === 'list' ? React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 600,
      fontSize: '9px',
      letterSpacing: 'var(--track-label)',
      textTransform: 'uppercase',
      color: 'var(--text-dim)'
    }
  }, props.kindLabel || 'LIVE LIST') : null, React.createElement('span', null, props.label), props.onDismiss ? React.createElement('button', {
    type: 'button',
    onClick: props.onDismiss,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    title: 'Drop this rule',
    style: {
      border: 'none',
      background: 'transparent',
      padding: 0,
      width: '14px',
      height: '14px',
      cursor: 'pointer',
      color: hover ? 'var(--text)' : 'var(--text-dim)',
      fontSize: '12px',
      lineHeight: 1
    }
  }, '\u2715') : null);
}
Object.assign(__ds_scope, { CutChip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/CutChip.jsx", error: String((e && e.message) || e) }); }

// components/feedback/DockCard.jsx
try { (() => {
const React = window.React;

/* The ambient dock, bottom-left (the right edge belongs to the filter panel
   and the scrollbars). Non-modal, non-blocking, and it never takes focus:
   the journal prompt and the launch strip both live here. The dismiss is a
   bare ×, deliberately not a button reading "Skip" — naming the act of
   ignoring something makes ignoring it a decision. */
function DockCard(props) {
  const [hover, setHover] = React.useState(false);
  return React.createElement('div', {
    style: Object.assign({
      width: props.width || '352px',
      padding: '12px 14px',
      background: 'var(--surface-raised)',
      border: '1px solid var(--line)',
      borderRadius: '10px',
      boxShadow: 'var(--shadow-dock)'
    }, props.style)
  }, props.title === undefined && !props.onDismiss ? null : React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      marginBottom: '10px'
    }
  }, React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 500,
      fontSize: '13px',
      color: 'var(--text)',
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      textOverflow: 'ellipsis'
    }
  }, props.title), props.meta === undefined ? null : React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data-s)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)'
    }
  }, props.meta), React.createElement('span', {
    style: {
      flex: 1
    }
  }), props.onDismiss ? React.createElement('button', {
    type: 'button',
    title: 'Dismiss',
    onClick: props.onDismiss,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      width: '22px',
      height: '22px',
      padding: 0,
      border: '1px solid transparent',
      borderRadius: 'var(--radius-control)',
      background: hover ? 'var(--surface-high)' : 'transparent',
      color: hover ? 'var(--text)' : 'var(--text-dim)',
      fontSize: '13px',
      lineHeight: 1,
      cursor: 'pointer'
    }
  }, '\u2715') : null), props.children);
}
Object.assign(__ds_scope, { DockCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/DockCard.jsx", error: String((e && e.message) || e) }); }

// components/feedback/EmptyState.jsx
try { (() => {
const React = window.React;

/* Empty states are DIRECTIONS, NOT MOODS. The app knows something faintly
   embarrassing about the user and must never be smug about it, so an empty
   surface says what fills it or where to go next. */
function EmptyState(props) {
  return React.createElement('div', {
    style: Object.assign({
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      gap: '18px',
      padding: '32px',
      textAlign: 'center'
    }, props.style)
  }, React.createElement('p', {
    style: {
      margin: 0,
      maxWidth: props.measure || '440px',
      fontFamily: 'var(--type-ui)',
      fontWeight: 500,
      fontSize: 'var(--text-body-l)',
      lineHeight: '22px',
      color: 'var(--text-dim)'
    }
  }, props.message), props.action || null);
}
Object.assign(__ds_scope, { EmptyState });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/EmptyState.jsx", error: String((e && e.message) || e) }); }

// components/feedback/RatingDots.jsx
try { (() => {
const React = window.React;

/* Five dots, not stars and not a number: it reads as optional when unset,
   which is the whole point of a prompt that must never feel owed. */
function RatingDots(props) {
  const value = props.value || 0;
  const [hover, setHover] = React.useState(0);
  return React.createElement('div', {
    style: Object.assign({
      display: 'flex',
      gap: '2px'
    }, props.style)
  }, [1, 2, 3, 4, 5].map(n => React.createElement('button', {
    key: n,
    type: 'button',
    title: n + ' out of 5',
    onClick: props.onChange ? () => props.onChange(n) : undefined,
    onMouseEnter: () => setHover(n),
    onMouseLeave: () => setHover(0),
    style: {
      width: '20px',
      height: '20px',
      padding: 0,
      border: 'none',
      borderRadius: '50%',
      background: 'transparent',
      cursor: 'pointer',
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center'
    }
  }, React.createElement('span', {
    style: {
      width: '11px',
      height: '11px',
      borderRadius: '50%',
      background: n <= value ? 'var(--volt)' : 'transparent',
      border: '1.5px solid ' + (n <= value ? 'var(--volt)' : n <= hover ? 'var(--text-dim)' : 'var(--text-faint)')
    }
  }))));
}
Object.assign(__ds_scope, { RatingDots });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/RatingDots.jsx", error: String((e && e.message) || e) }); }

// components/feedback/StatusPip.jsx
try { (() => {
const React = window.React;

/* The launch strip's dot. Volt, never Flare — a game starting up is the most
   active thing there is, and Flare belongs to unread updates alone. It
   pulses while Winnow is waiting for the process and stops when the game is
   confirmed up; Amber when something needs attention. */
function StatusPip(props) {
  const state = props.state || 'waiting';
  const fill = state === 'problem' ? 'var(--amber)' : 'var(--volt)';
  return React.createElement(React.Fragment, null, React.createElement('style', null, '@keyframes winnow-pip{0%{opacity:1}50%{opacity:.25}100%{opacity:1}}'), React.createElement('span', {
    style: Object.assign({
      display: 'inline-block',
      width: 'var(--pip-size)',
      height: 'var(--pip-size)',
      borderRadius: '50%',
      background: fill,
      flex: 'none',
      animation: state === 'waiting' ? 'winnow-pip var(--dur-pulse) infinite' : 'none',
      animationTimingFunction: 'var(--ease-pulse)'
    }, props.style)
  }));
}
Object.assign(__ds_scope, { StatusPip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/feedback/StatusPip.jsx", error: String((e && e.message) || e) }); }

// components/library/FeedCard.jsx
try { (() => {
const React = window.React;

/* THE REASON IS THE PRODUCT. Every recommendation carries a sentence — "You
   put 2.8 hours into this in 2021 and it has had an update since, most
   recently PATCH NOTES – S06.05.02." Not a genre tag, not a star rating.

   Numbers inside that sentence set in Plex Mono with tabular figures, which
   is the same rule every other number in the app follows: any
   whitespace-delimited word containing a digit is data, and the sentence's
   own punctuation at the edges stays prose. */
const EDGE = '"\u2018\u2019\u201c\u201d(),.;:!?\u2014\u2013-';
function splitReason(reason) {
  if (!reason) return [];
  const runs = [];
  const push = (text, isData) => {
    if (!text) return;
    const last = runs[runs.length - 1];
    if (last && last.isData === isData) last.text += text;else runs.push({
      text: text,
      isData: isData
    });
  };
  reason.split(/(\s+)/).forEach(word => {
    if (!/\d/.test(word)) {
      push(word, false);
      return;
    }
    let start = 0;
    while (start < word.length && EDGE.indexOf(word[start]) >= 0) start++;
    let end = word.length;
    while (end > start && EDGE.indexOf(word[end - 1]) >= 0) end--;
    push(word.slice(0, start), false);
    push(word.slice(start, end), true);
    push(word.slice(end), false);
  });
  return runs;
}
function ramp(monthsIdle) {
  const t = Math.min(1, Math.max(0, (monthsIdle || 0) / 36));
  return 'saturate(' + (1 - 0.78 * t).toFixed(2) + ') hue-rotate(' + (-6 * t).toFixed(0) + 'deg) brightness(' + (1 - 0.32 * t).toFixed(2) + ')';
}
function Act(props) {
  const [hover, setHover] = React.useState(false);
  const primary = !!props.primary;
  const quiet = !!props.quiet;
  return React.createElement('button', {
    type: 'button',
    onClick: props.onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 600,
      fontSize: primary || quiet ? '12px' : '11px',
      padding: primary || quiet ? '6px 14px' : '5px 8px',
      borderRadius: 'var(--radius-control)',
      borderWidth: '2px',
      borderStyle: 'solid',
      cursor: 'pointer',
      whiteSpace: 'nowrap',
      background: primary ? hover ? 'var(--volt-hover)' : 'var(--volt)' : hover ? quiet ? 'var(--surface-raised)' : 'var(--surface-high)' : 'transparent',
      borderColor: primary ? hover ? 'var(--volt-hover)' : 'var(--volt)' : quiet ? 'var(--line)' : 'transparent',
      color: primary ? 'var(--volt-ink)' : quiet ? 'var(--text)' : hover ? 'var(--text)' : 'var(--text-dim)'
    }
  }, props.children);
}
function FeedCard(props) {
  const [hover, setHover] = React.useState(false);
  const setAside = !!props.setAsideNote;
  const art = props.cover ? {
    backgroundImage: 'url(' + props.cover + ')',
    backgroundSize: 'cover',
    backgroundPosition: 'center'
  } : {
    background: props.gradient || 'linear-gradient(155deg,var(--surface-raised),var(--surface))'
  };
  return React.createElement('div', {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: Object.assign({
      display: 'grid',
      gridTemplateColumns: '108px 1fr',
      gap: '14px',
      padding: '14px',
      background: hover ? 'var(--surface-raised)' : 'var(--surface)',
      border: '1px solid var(--line-soft)',
      borderRadius: 'var(--radius-tile)',
      cursor: 'pointer',
      transition: 'background var(--dur-hover-restore) linear'
    }, props.style)
  }, React.createElement('div', {
    style: {
      position: 'relative',
      width: '108px',
      height: '162px',
      opacity: setAside ? 0.42 : 1
    }
  }, React.createElement('div', {
    style: Object.assign({
      position: 'absolute',
      inset: 0,
      borderRadius: 'var(--radius-tile)',
      border: '1px solid var(--line-soft)',
      overflow: 'hidden',
      filter: hover ? 'none' : ramp(props.monthsIdle),
      transition: 'filter var(--dur-hover-restore) var(--ease-out)'
    }, art)
  }, props.cover ? null : React.createElement('div', {
    style: {
      position: 'absolute',
      left: '10px',
      right: '10px',
      bottom: '10px',
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '15px',
      lineHeight: '17px',
      color: '#fff',
      textShadow: '0 2px 14px rgba(0,0,0,.65)'
    }
  }, props.title), React.createElement('div', {
    style: {
      position: 'absolute',
      inset: 0,
      background: 'var(--tile-gloss)'
    }
  })), props.unread ? React.createElement('span', {
    title: props.unreadTooltip || 'Patched since you played',
    style: {
      position: 'absolute',
      top: '6px',
      right: '6px',
      width: '14px',
      height: '14px',
      borderRadius: '50%',
      background: 'var(--flare)',
      border: '2px solid var(--ground)',
      boxShadow: 'var(--badge-glow)'
    }
  }) : null), React.createElement('div', {
    style: {
      display: 'flex',
      flexDirection: 'column',
      minWidth: 0
    }
  }, React.createElement('div', {
    style: {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '15px',
      lineHeight: '17px',
      color: 'var(--text)',
      marginBottom: '6px'
    }
  }, props.title), React.createElement('p', {
    style: {
      margin: 0,
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      lineHeight: '18px',
      color: 'var(--text)'
    }
  }, splitReason(props.reason).map((run, i) => run.isData ? React.createElement('span', {
    key: i,
    style: {
      fontFamily: 'var(--type-number)',
      fontVariantNumeric: 'tabular-nums'
    }
  }, run.text) : run.text)), React.createElement('div', {
    style: {
      flex: 1,
      minHeight: '10px'
    }
  }), setAside ? React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '5px'
    }
  }, React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      color: 'var(--text-dim)'
    }
  }, props.setAsideNote), props.setAsideDate ? React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)'
    }
  }, props.setAsideDate) : null, React.createElement('span', {
    style: {
      flex: 1
    }
  }), React.createElement(Act, {
    quiet: true,
    onClick: props.onUndo
  }, 'Undo')) : React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '8px'
    }
  }, props.primaryLabel ? React.createElement(Act, {
    primary: true,
    onClick: props.onPrimary
  }, props.primaryLabel) : null, React.createElement(Act, {
    onClick: props.onNotInterested
  }, 'Not interested'), React.createElement(Act, {
    onClick: props.onNotNow
  }, 'Not now'), React.createElement('span', {
    style: {
      flex: 1
    }
  }), props.store ? React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: '9px',
      letterSpacing: 'var(--track-badge)',
      color: 'var(--text-dim)',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-badge)',
      padding: '1px 5px'
    }
  }, props.store) : null)));
}
Object.assign(__ds_scope, { FeedCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/library/FeedCard.jsx", error: String((e && e.message) || e) }); }

// components/library/GameTile.jsx
try { (() => {
const React = window.React;

/* THE ART IS THE CHART. A tile's cover is desaturated in proportion to how
   long the game has sat unplayed, and hover restores it over 140ms — the
   single most important interaction in the app, because it shows you the
   before and the after and makes the encoding legible.

   The ramp: saturate() then hue-rotate(-6deg) then brightness(), clamped at
   0.22 / 0.68. Never fully grey — a cover you cannot identify is a cover you
   cannot choose, and the point is to make forgotten games findable. */
function ramp(monthsIdle) {
  const t = Math.min(1, Math.max(0, (monthsIdle || 0) / 36));
  const sat = 1 - (1 - 0.22) * t;
  const bright = 1 - (1 - 0.68) * t;
  return 'saturate(' + sat.toFixed(2) + ') hue-rotate(' + (-6 * t).toFixed(0) + 'deg) brightness(' + bright.toFixed(2) + ')';
}
function TileButton(props) {
  const [hover, setHover] = React.useState(false);
  const primary = !!props.primary;
  return React.createElement('button', {
    type: 'button',
    title: props.tooltip,
    onClick: props.onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      width: '100%',
      padding: '5px 0',
      fontFamily: 'var(--type-ui)',
      fontWeight: 600,
      fontSize: '11px',
      borderRadius: 'var(--radius-control)',
      borderWidth: '2px',
      borderStyle: 'solid',
      cursor: 'pointer',
      background: primary ? hover ? 'var(--volt-hover)' : 'var(--volt)' : hover ? 'var(--surface-raised)' : 'transparent',
      borderColor: primary ? hover ? 'var(--volt-hover)' : 'var(--volt)' : hover ? 'var(--text-dim)' : 'var(--line)',
      color: primary ? 'var(--volt-ink)' : 'var(--text)'
    }
  }, props.children);
}
function GameTile(props) {
  const [hover, setHover] = React.useState(false);
  const [flipped, setFlipped] = React.useState(false);
  const width = props.width || 148;
  const dimming = props.dimming === undefined ? true : props.dimming;
  const art = props.cover ? {
    backgroundImage: 'url(' + props.cover + ')',
    backgroundSize: 'cover',
    backgroundPosition: 'center'
  } : {
    background: props.gradient || 'linear-gradient(155deg,var(--surface-raised),var(--surface))'
  };
  const filter = !dimming || hover ? 'none' : ramp(props.monthsIdle);
  const face = React.createElement('div', {
    style: {
      position: 'absolute',
      inset: 0,
      borderRadius: 'var(--radius-tile)',
      overflow: 'hidden',
      border: '1px solid var(--line-soft)',
      background: 'var(--tile-ground)',
      opacity: flipped ? 0 : 1,
      transform: flipped ? 'scaleX(0)' : 'scaleX(1)',
      transition: 'opacity var(--dur-flip) var(--ease-in), transform var(--dur-flip) var(--ease-in)'
    }
  }, React.createElement('div', {
    style: Object.assign({
      position: 'absolute',
      inset: 0,
      filter: filter,
      transition: 'filter var(--dur-hover-restore) var(--ease-out)'
    }, art)
  }, props.cover ? null : React.createElement('div', {
    style: {
      position: 'absolute',
      left: '14px',
      right: '14px',
      bottom: '14px',
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '19px',
      lineHeight: '20px',
      color: '#fff',
      textShadow: '0 2px 14px rgba(0,0,0,.65)',
      opacity: hover ? 0 : 1,
      transition: 'opacity var(--dur-hover-restore) var(--ease-out)'
    }
  }, props.title)), React.createElement('div', {
    style: {
      position: 'absolute',
      inset: 0,
      background: 'var(--tile-gloss)'
    }
  }), React.createElement('div', {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      bottom: 0,
      padding: '24px 10px 10px',
      background: 'var(--tile-scrim)',
      opacity: hover ? 1 : 0,
      transition: 'opacity var(--dur-hover-restore) var(--ease-out)'
    }
  }, React.createElement('div', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 500,
      fontSize: 'var(--text-body-l)',
      lineHeight: '18px',
      color: 'var(--text)',
      marginBottom: '8px'
    }
  }, props.title), React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: '6px'
    }
  }, React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data-s)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)'
    }
  }, props.stat || [props.playtime, props.idle ? 'idle ' + props.idle : null].filter(Boolean).join(' · ')), props.store ? React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: '9px',
      letterSpacing: 'var(--track-badge)',
      color: 'var(--text-dim)',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-badge)',
      padding: '1px 5px'
    }
  }, props.store) : null)));
  const back = React.createElement('div', {
    style: {
      position: 'absolute',
      inset: 0,
      display: 'flex',
      flexDirection: 'column',
      justifyContent: 'space-between',
      gap: '8px',
      padding: '10px',
      background: 'var(--surface)',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-tile)',
      opacity: flipped ? 1 : 0,
      transform: flipped ? 'scaleX(1)' : 'scaleX(0)',
      pointerEvents: flipped ? 'auto' : 'none',
      transition: 'opacity var(--dur-flip) var(--ease-out) var(--dur-flip), transform var(--dur-flip) var(--ease-out) var(--dur-flip)'
    }
  }, React.createElement('div', {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '5px',
      minWidth: 0
    }
  }, React.createElement('div', {
    style: {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '13px',
      lineHeight: '15px',
      color: 'var(--text)'
    }
  }, props.title), React.createElement('div', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 600,
      fontSize: '10px',
      letterSpacing: 'var(--track-label)',
      textTransform: 'uppercase',
      color: 'var(--text-dim)'
    }
  }, props.bucketLabel || ''), ['PLAYED', 'LAST'].map((k, i) => React.createElement('div', {
    key: k,
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: '6px'
    }
  }, React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontWeight: 600,
      fontSize: '9px',
      letterSpacing: 'var(--track-label)',
      color: 'var(--text-dim)'
    }
  }, k), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data-s)',
      fontVariantNumeric: 'tabular-nums',
      color: i === 0 ? 'var(--text)' : 'var(--text-dim)'
    }
  }, i === 0 ? props.playtime : props.lastPlayed || props.idle))), React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: '6px'
    }
  }, React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data-s)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)'
    }
  }, props.releaseYear || ''), props.store ? React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: '9px',
      letterSpacing: 'var(--track-badge)',
      color: 'var(--text-dim)',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-badge)',
      padding: '1px 5px'
    }
  }, props.store) : null)), React.createElement('div', {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '4px'
    }
  }, props.primaryLabel ? React.createElement(TileButton, {
    primary: true,
    tooltip: props.primaryHint,
    onClick: props.onPrimary
  }, props.primaryLabel) : null, React.createElement(TileButton, {
    tooltip: 'Put this title in a list',
    onClick: props.onAddToList
  }, 'Add to list'), React.createElement(TileButton, {
    tooltip: 'Full details',
    onClick: props.onOpenDetails
  }, 'Details')));
  return React.createElement('div', {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    onClick: () => setFlipped(!flipped),
    style: Object.assign({
      position: 'relative',
      width: typeof width === 'number' ? width + 'px' : width,
      aspectRatio: '2 / 3',
      borderRadius: 'var(--radius-tile)',
      cursor: 'pointer',
      transform: hover && !flipped ? 'translateY(-2px)' : 'translateY(0)',
      boxShadow: hover && !flipped ? 'var(--shadow-tile-hover)' : 'none',
      transition: 'transform var(--dur-hover-restore) var(--ease-out), box-shadow var(--dur-hover-restore) var(--ease-out)'
    }, props.style)
  }, face, back, props.selected ? React.createElement('div', {
    style: {
      position: 'absolute',
      inset: 0,
      border: '2px solid var(--volt)',
      borderRadius: 'var(--radius-tile)',
      pointerEvents: 'none'
    }
  }, React.createElement('div', {
    style: {
      position: 'absolute',
      inset: 0,
      border: '1px solid var(--ground)',
      borderRadius: '4px'
    }
  })) : null, props.unread ? React.createElement('span', {
    title: props.unreadTooltip || 'Patched since you played',
    style: {
      position: 'absolute',
      top: '8px',
      right: '8px',
      width: '14px',
      height: '14px',
      borderRadius: '50%',
      background: 'var(--flare)',
      border: '2px solid var(--ground)',
      boxShadow: 'var(--badge-glow)',
      opacity: flipped ? 0 : 1,
      transition: 'opacity var(--dur-flip) var(--ease-in)'
    }
  }) : null);
}
Object.assign(__ds_scope, { GameTile });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/library/GameTile.jsx", error: String((e && e.message) || e) }); }

// components/library/GapRail.jsx
try { (() => {
const React = window.React;

/* THE ONE THING WINNOW CAN DRAW THAT NOTHING ELSE CAN. Storefronts hold your
   last-played date and they hold a game's patch history; nobody puts them on
   the same axis. The rail runs from your last session to now, with the
   updates that landed in between marked on it.

   It is §5.1's dormancy ramp turned on its side: Volt at the last-played end
   fading to Line at today. Marks are Flare, legal here because they are
   literally the unread signal plotted in time. NORMALISED, never scaled to
   duration: a 14-day gap and a 9-year gap draw the same length, with the
   span stated as a number beside it. Capped at 14 marks — past that a rail
   is a smear, and the update list below stays the exhaustive record. */
function GapRail(props) {
  const marks = (props.marks || []).slice(0, 14);
  return React.createElement('div', {
    style: Object.assign({
      position: 'relative',
      height: '12px'
    }, props.style)
  }, React.createElement('div', {
    style: {
      position: 'absolute',
      left: 0,
      right: 0,
      top: '5px',
      height: '2px',
      borderRadius: '1px',
      background: 'var(--gap-rail)'
    }
  }), React.createElement('div', {
    style: {
      position: 'absolute',
      left: 0,
      top: '1px',
      width: '2px',
      height: '10px',
      background: 'var(--volt)'
    }
  }), React.createElement('div', {
    style: {
      position: 'absolute',
      right: 0,
      top: '1px',
      width: '2px',
      height: '10px',
      background: 'var(--line)'
    }
  }), marks.map((m, i) => React.createElement('span', {
    key: i,
    title: props.markTooltip || 'An update landed here',
    style: {
      position: 'absolute',
      left: 'calc(' + Math.min(100, Math.max(0, m * 100)) + '% - 3.5px)',
      top: '2px',
      width: '7px',
      height: '7px',
      borderRadius: '50%',
      background: 'var(--flare)',
      border: '1px solid var(--surface)',
      boxShadow: 'var(--pip-glow)'
    }
  })));
}
Object.assign(__ds_scope, { GapRail });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/library/GapRail.jsx", error: String((e && e.message) || e) }); }

// components/library/LibraryRow.jsx
try { (() => {
const React = window.React;

/* List view: the same data with no art dependency. 44px rows, Line rules, a
   2px Volt selection edge, and every number in Plex Mono with tabular
   figures — a playtime column that does not align vertically is unreadable
   at scan speed. This is the power-user view, which is how the analytics
   capability stays available without dominating the default experience. */
function LibraryRow(props) {
  const [hover, setHover] = React.useState(false);
  const bg = props.selected ? 'var(--chrome-raised)' : hover ? 'var(--chrome-raised-half)' : 'transparent';
  return React.createElement('div', {
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    onClick: props.onClick,
    style: Object.assign({
      display: 'grid',
      gridTemplateColumns: '2px 18px 1fr 84px 104px 92px 24px',
      alignItems: 'center',
      height: 'var(--list-row-height)',
      borderBottom: '1px solid var(--line)',
      background: bg,
      cursor: 'pointer'
    }, props.style)
  }, React.createElement('span', {
    style: {
      alignSelf: 'stretch',
      background: props.selected ? 'var(--volt)' : 'transparent'
    }
  }), React.createElement('span', {
    style: {
      paddingLeft: '6px'
    }
  }, props.unread ? React.createElement('span', {
    title: 'Patched since you played',
    style: {
      display: 'block',
      width: '8px',
      height: '8px',
      borderRadius: '50%',
      background: 'var(--flare)',
      boxShadow: 'var(--pip-glow)'
    }
  }) : null), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: 'var(--text-body-l)',
      color: 'var(--text)',
      paddingRight: '12px',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, props.title), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: '11px',
      color: 'var(--text-dim)'
    }
  }, props.store), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text)',
      textAlign: 'right'
    }
  }, props.playtime), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)',
      textAlign: 'right'
    }
  }, props.idle), React.createElement('span', null));
}
Object.assign(__ds_scope, { LibraryRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/library/LibraryRow.jsx", error: String((e && e.message) || e) }); }

// components/library/SectionPanel.jsx
try { (() => {
const React = window.React;

/* A feed shelf: an outlined island, no fill. Its heading is Bricolage at
   17px — the app's own voice making a claim — but never display-l, which is
   the screen's own name and has to outrank it. The blurb underneath is the
   sentence every card in the section is an instance of. */
function SectionPanel(props) {
  return React.createElement('section', {
    style: Object.assign({
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-pane)'
    }, props.style)
  }, React.createElement('div', {
    style: {
      margin: '15px 18px 14px'
    }
  }, React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '10px'
    }
  }, React.createElement('h2', {
    style: {
      margin: 0,
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '17px',
      color: 'var(--text)'
    }
  }, props.title), props.count === undefined ? null : React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: 'var(--text-data-s)',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)'
    }
  }, props.count)), props.blurb ? React.createElement('p', {
    style: {
      margin: '3px 0 0',
      maxWidth: '720px',
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      lineHeight: '19px',
      color: 'var(--text-dim)'
    }
  }, props.blurb) : null), React.createElement('div', {
    style: {
      height: '1px',
      background: 'var(--line)'
    }
  }), React.createElement('div', {
    style: {
      padding: props.pad || '16px'
    }
  }, props.children));
}
Object.assign(__ds_scope, { SectionPanel });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/library/SectionPanel.jsx", error: String((e && e.message) || e) }); }

// components/navigation/RailRow.jsx
try { (() => {
const React = window.React;

/* The rail's row, in two voices. A BUCKET is the application's own
   vocabulary and is shouted — Display S caps. A LIST is the user's own
   sentence and is not — body type. Both take the same fill and the same 2px
   Volt edge, because a list is another way of cutting the same library.

   Three states, and the edge is the grammar: Volt means THIS IS WHERE YOU
   ARE, and exactly one row in the rail ever has it. A bucket in force while
   a list is open takes the fill with a TextDim edge instead. */
function RailRow(props) {
  const [hover, setHover] = React.useState(false);
  const bucket = (props.kind || 'bucket') === 'bucket';
  const state = props.state || 'default';
  const lit = state === 'selected' || state === 'rule' || hover;
  return React.createElement('button', {
    type: 'button',
    title: props.tooltip,
    onClick: props.onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: Object.assign({
      display: 'grid',
      gridTemplateColumns: '2px 16px auto 1fr auto 16px',
      alignItems: 'center',
      width: '100%',
      height: bucket ? 'var(--rail-row-height)' : 'var(--rail-list-row-height)',
      padding: 0,
      border: 'none',
      borderRadius: 0,
      background: lit ? 'var(--chrome-raised)' : 'transparent',
      cursor: 'pointer',
      textAlign: 'left',
      opacity: props.dim ? 0.4 : 1,
      transition: 'background var(--dur-fill) linear'
    }, props.style)
  }, React.createElement('span', {
    style: {
      alignSelf: 'stretch',
      background: state === 'selected' ? 'var(--volt)' : state === 'rule' ? 'var(--text-dim)' : 'transparent'
    }
  }), React.createElement('span', null), props.pip ? React.createElement('span', {
    title: 'Games patched since you played them',
    style: {
      width: 'var(--pip-size)',
      height: 'var(--pip-size)',
      borderRadius: '50%',
      marginRight: '8px',
      background: 'var(--flare)',
      boxShadow: 'var(--pip-glow)'
    }
  }) : React.createElement('span', null), React.createElement('span', {
    style: bucket ? {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: 'var(--text-display-s)',
      letterSpacing: 'var(--track-display-s)',
      textTransform: 'uppercase',
      color: 'var(--text)',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    } : {
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      color: 'var(--text)',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, props.label), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-number)',
      fontSize: '11px',
      fontVariantNumeric: 'tabular-nums',
      color: 'var(--text-dim)'
    }
  }, props.count === undefined || props.count === null ? '' : props.count), React.createElement('span', null));
}
Object.assign(__ds_scope, { RailRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/RailRow.jsx", error: String((e && e.message) || e) }); }

// components/navigation/SegmentedToggle.jsx
try { (() => {
const React = window.React;

/* Grid or list. The segmented control is the search box's twin — same
   height, same border, same field fill — and Volt marks the active glyph
   because a view choice is a selection. */
function GridGlyph(props) {
  return React.createElement('span', {
    style: {
      display: 'grid',
      gridTemplateColumns: '5px 5px',
      gridTemplateRows: '5px 5px',
      gap: '2px'
    }
  }, [0, 1, 2, 3].map(i => React.createElement('span', {
    key: i,
    style: {
      width: '5px',
      height: '5px',
      background: props.fill
    }
  })));
}
function ListGlyph(props) {
  return React.createElement('span', {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '3px',
      width: '13px'
    }
  }, [0, 1, 2].map(i => React.createElement('span', {
    key: i,
    style: {
      height: '2px',
      width: '13px',
      background: props.fill
    }
  })));
}
function SegmentedToggle(props) {
  const value = props.value || 'grid';
  const options = props.options || [{
    id: 'grid',
    tooltip: 'Grid'
  }, {
    id: 'list',
    tooltip: 'List'
  }];
  return React.createElement('div', {
    style: Object.assign({
      display: 'inline-flex',
      flex: 'none',
      height: '30px',
      overflow: 'hidden',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-control)',
      background: 'var(--field-on-ground)'
    }, props.style)
  }, options.map(o => {
    const on = o.id === value;
    const fill = on ? 'var(--volt)' : 'var(--text-dim)';
    return React.createElement('button', {
      key: o.id,
      type: 'button',
      title: o.tooltip,
      onClick: props.onChange ? () => props.onChange(o.id) : undefined,
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '5px 10px',
        flex: 'none',
        border: 'none',
        background: on ? 'var(--chrome-raised)' : 'transparent',
        cursor: 'pointer'
      }
    }, o.label ? React.createElement('span', {
      style: {
        fontFamily: 'var(--type-ui)',
        fontSize: '13px',
        color: on ? 'var(--volt)' : 'var(--text-dim)'
      }
    }, o.label) : o.id === 'grid' ? React.createElement(GridGlyph, {
      fill: fill
    }) : React.createElement(ListGlyph, {
      fill: fill
    }));
  }));
}
Object.assign(__ds_scope, { SegmentedToggle });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/SegmentedToggle.jsx", error: String((e && e.message) || e) }); }

// components/navigation/SortMenu.jsx
try { (() => {
const React = window.React;

/* A small dark card of choices, drawn in the window's own tree rather than a
   popup: this app avoids flyouts wherever it can, because a popup is its own
   root and the focus ring cannot render inside one. A 6px Volt dot marks the
   order in force. */
function Item(props) {
  const [hover, setHover] = React.useState(false);
  return React.createElement('button', {
    type: 'button',
    onClick: props.onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: 'grid',
      gridTemplateColumns: '14px 1fr',
      alignItems: 'center',
      width: '100%',
      height: '30px',
      padding: '0 8px',
      border: 'none',
      borderRadius: 'var(--radius-control)',
      background: hover ? 'var(--ground-veil)' : 'transparent',
      cursor: 'pointer',
      textAlign: 'left'
    }
  }, React.createElement('span', {
    style: {
      width: '6px',
      height: '6px',
      borderRadius: '50%',
      background: props.selected ? 'var(--volt)' : 'transparent'
    }
  }), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-ui)',
      fontSize: 'var(--text-body)',
      color: 'var(--text)'
    }
  }, props.label));
}
function SortMenu(props) {
  return React.createElement('div', {
    style: Object.assign({
      minWidth: '176px',
      maxWidth: '320px',
      padding: '4px',
      background: 'var(--surface-raised)',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-control)'
    }, props.style)
  }, (props.options || []).map(o => React.createElement(Item, {
    key: o.id,
    label: o.label,
    selected: o.id === props.value,
    onClick: props.onChange ? () => props.onChange(o.id) : undefined
  })));
}
Object.assign(__ds_scope, { SortMenu });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/SortMenu.jsx", error: String((e && e.message) || e) }); }

// components/navigation/TitleBar.jsx
try { (() => {
const React = window.React;

/* The app draws its own 36px lip. Nothing else lives here — no menu, no
   search, no status: it is a lip, not a toolbar. The mark is the dragon at
   20px in TextDim, so it reads as one lockup with the wordmark. Close is the
   one caption convention worth keeping: it reddens, because it is the only
   button here whose mistake cannot be undone. */
function CaptionButton(props) {
  const [hover, setHover] = React.useState(false);
  const [press, setPress] = React.useState(false);
  const danger = props.kind === 'close';
  const bg = press ? danger ? 'var(--danger-press)' : 'var(--surface-high)' : hover ? danger ? 'var(--danger)' : 'var(--surface-raised)' : 'transparent';
  const stroke = hover ? danger ? 'var(--danger-ink)' : 'var(--text)' : 'var(--text-dim)';
  return React.createElement('button', {
    type: 'button',
    title: props.tooltip,
    onClick: props.onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => {
      setHover(false);
      setPress(false);
    },
    onMouseDown: () => setPress(true),
    onMouseUp: () => setPress(false),
    style: {
      width: 'var(--window-button-width)',
      height: 'var(--title-bar-height)',
      border: 'none',
      padding: 0,
      background: bg,
      cursor: 'default',
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      transition: 'background var(--dur-caption) linear'
    }
  }, React.createElement('svg', {
    width: 11,
    height: 11,
    viewBox: '0 0 11 11'
  }, React.createElement('path', {
    d: props.glyph,
    fill: 'none',
    stroke: stroke,
    strokeWidth: 1
  })));
}
const GLYPHS = {
  minimise: 'M 0,5.5 L 10,5.5',
  maximise: 'M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z',
  restore: 'M 2.5,2.5 L 2.5,0.5 L 10.5,0.5 L 10.5,8.5 L 8.5,8.5 M 0.5,2.5 L 8.5,2.5 L 8.5,10.5 L 0.5,10.5 Z',
  close: 'M 0.5,0.5 L 9.5,9.5 M 9.5,0.5 L 0.5,9.5'
};
function TitleBar(props) {
  const mark = props.markSrc || 'assets/icons/dragon-mark.svg';
  return React.createElement('div', {
    style: Object.assign({
      height: 'var(--title-bar-height)',
      background: props.layout === 'floating' ? 'transparent' : 'var(--caption-fill)',
      display: 'flex',
      alignItems: 'center',
      flex: 'none'
    }, props.style)
  }, React.createElement('div', {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '9px',
      marginLeft: '12px'
    }
  }, React.createElement('img', {
    src: mark,
    alt: '',
    width: 20,
    height: 20,
    style: {
      display: 'block'
    }
  }), React.createElement('span', {
    style: {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '11px',
      letterSpacing: 'var(--track-wordmark)',
      textTransform: 'uppercase',
      color: 'var(--text-dim)'
    }
  }, props.title || 'WINNOW')), React.createElement('div', {
    style: {
      flex: 1
    }
  }), React.createElement(CaptionButton, {
    kind: 'minimise',
    glyph: GLYPHS.minimise,
    tooltip: 'Minimise'
  }), React.createElement(CaptionButton, {
    kind: 'maximise',
    glyph: props.maximised ? GLYPHS.restore : GLYPHS.maximise,
    tooltip: props.maximised ? 'Restore down' : 'Maximise'
  }), React.createElement(CaptionButton, {
    kind: 'close',
    glyph: GLYPHS.close,
    tooltip: 'Close'
  }));
}
Object.assign(__ds_scope, { TitleBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/navigation/TitleBar.jsx", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/app.jsx
try { (() => {
const {
  TitleBar,
  DockCard,
  RatingDots,
  StatusPip,
  TextField,
  Button,
  EmptyState,
  Badge
} = window.WinnowDesignSystem_df253a;
const BUCKET_LABEL = {
  patched: 'Patched since',
  never: 'Never played',
  bounced: 'Bounced off',
  playedout: 'Played out',
  wontrun: "Won't run"
};
const EMPTY = {
  patched: "Nothing's been patched since you last played. This fills up on its own.",
  never: "You've played everything you own. Genuinely rare.",
  wontrun: "Nothing here. Winnow marks a title Won't run only after a launch that never started."
};

/* Merge confirm queue (§6): two covers, the signals between them, and two
   answers by name. Never Merge / Cancel — that asks the user to reason
   about the data model instead of about games. */
function MergeQueue() {
  const side = (title, store, year, g) => /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '10px',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      width: '200px',
      height: '300px',
      borderRadius: 'var(--radius-tile)',
      border: '1px solid var(--line-soft)',
      background: window.grad(g),
      display: 'flex',
      alignItems: 'flex-end',
      padding: '14px'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '19px',
      lineHeight: '20px',
      color: '#fff',
      textShadow: '0 2px 14px rgba(0,0,0,.65)'
    }
  }, title)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: '8px',
      alignItems: 'center'
    }
  }, /*#__PURE__*/React.createElement(Badge, null, store), /*#__PURE__*/React.createElement("span", {
    className: "data-s"
  }, year)));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      background: 'var(--pane-ground)',
      overflowY: 'auto'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      padding: '20px 24px 18px',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("h1", {
    className: "display-l",
    style: {
      margin: 0
    }
  }, "Same game?"), /*#__PURE__*/React.createElement("p", {
    className: "body",
    style: {
      margin: '7px 0 0',
      maxWidth: '640px',
      fontSize: '12px',
      lineHeight: '18px',
      color: 'var(--text-dim)'
    }
  }, "Winnow never merges on a fuzzy title. Hard id joins happen on their own; anything else waits here for you.")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: '28px',
      padding: '24px',
      alignItems: 'flex-start'
    }
  }, side('Elder Scrolls V: Skyrim', 'STEAM', '2011', ['#57534E', '#292524']), side('Skyrim Special Edition', 'GOG', '2016', ['#44403C', '#1C1917']), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minWidth: '260px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "label",
    style: {
      marginBottom: '10px'
    }
  }, "Signals"), [['Title distance', '0.86'], ['Year delta', '5'], ['Publisher', 'Bethesda · Bethesda'], ['Achievement sets', 'differ']].map(s => /*#__PURE__*/React.createElement("div", {
    key: s[0],
    style: {
      display: 'flex',
      justifyContent: 'space-between',
      padding: '9px 0',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "body",
    style: {
      color: 'var(--text-dim)'
    }
  }, s[0]), /*#__PURE__*/React.createElement("span", {
    className: "data"
  }, s[1]))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: '10px',
      marginTop: '18px'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "primary"
  }, "Same game"), /*#__PURE__*/React.createElement(Button, {
    variant: "quiet"
  }, "Different games")), /*#__PURE__*/React.createElement("p", {
    className: "body",
    style: {
      margin: '14px 0 0',
      fontSize: '12px',
      lineHeight: '17px',
      color: 'var(--text-dim)'
    }
  }, "A release is not a work: Skyrim SE is not Skyrim, and the achievement sets differ."))));
}
function NotRecreated(props) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      background: 'var(--pane-ground)',
      display: 'flex'
    }
  }, /*#__PURE__*/React.createElement(EmptyState, {
    message: props.message
  }));
}
function App() {
  const d = window.WINNOW_DATA;
  const [screen, setScreen] = React.useState('library');
  const [bucket, setBucket] = React.useState(null);
  const [list, setList] = React.useState(null);
  const [view, setView] = React.useState('grid');
  const [density, setDensity] = React.useState(148);
  const [search, setSearch] = React.useState('');
  const [sort, setSort] = React.useState('dormant');
  const [rules, setRules] = React.useState([]);
  const [filtersOpen, setFiltersOpen] = React.useState(false);
  const [selected, setSelected] = React.useState(null);
  const [details, setDetails] = React.useState(null);
  const [launching, setLaunching] = React.useState(null);
  const [journal, setJournal] = React.useState(false);
  const [rating, setRating] = React.useState(0);
  React.useEffect(() => {
    const onKey = e => {
      if (e.key !== 'Escape') return;
      if (details) {
        setDetails(null);
        return;
      }
      if (filtersOpen) {
        setFiltersOpen(false);
        return;
      }
      if (rules.length) {
        setRules([]);
        return;
      }
      if (list) {
        setList(null);
        return;
      }
      if (bucket) setBucket(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [details, filtersOpen, rules, list, bucket]);
  const games = d.games.filter(g => {
    if (bucket && g.b !== bucket) return false;
    if (search && g.t.toLowerCase().indexOf(search.toLowerCase()) < 0) return false;
    if (rules.indexOf('Installed') >= 0 && !g.installed) return false;
    if (rules.indexOf('Not installed') >= 0 && g.installed) return false;
    return true;
  }).slice().sort((a, b) => sort === 'title' ? a.t.localeCompare(b.t) : sort === 'playtime' ? parseFloat(b.h) - parseFloat(a.h) : b.idle - a.idle);
  const chips = [];
  if (list) {
    const l = d.liveLists.concat(d.lists).find(x => x.id === list);
    const live = !!d.liveLists.find(x => x.id === list);
    chips.push({
      id: 'list:' + list,
      kind: 'list',
      label: l.name,
      kindLabel: live ? 'LIVE LIST' : 'LIST',
      tooltip: 'The place you are in. Its × leaves'
    });
    if (live) chips.push({
      id: 'inh:coop',
      kind: 'inherited',
      label: 'Co-op',
      tooltip: 'This live list set this rule, not you'
    });
  }
  if (bucket) chips.push({
    id: 'bucket',
    kind: 'user',
    label: BUCKET_LABEL[bucket],
    tooltip: 'You set this from the rail'
  });
  rules.forEach(r => chips.push({
    id: 'rule:' + r,
    kind: 'user',
    label: r,
    tooltip: 'You set this in the filter panel'
  }));
  const dropChip = id => {
    if (id === 'bucket') setBucket(null);else if (id.indexOf('list:') === 0) setList(null);else if (id.indexOf('rule:') === 0) setRules(rules.filter(r => r !== id.slice(5)));
  };
  const launch = title => {
    setLaunching(title);
    setDetails(null);
    window.setTimeout(() => {
      setLaunching(null);
      setJournal(true);
    }, 2600);
  };
  const toggleRule = label => setRules(rules.indexOf(label) >= 0 ? rules.filter(r => r !== label) : rules.concat([label]));

  /* MinWidth is the app's own honest fix (MainWindow.axaml): below 1200 the
     command bar does not degrade gracefully, it collides. The page scrolls
     horizontally rather than letting the bar paint over the filter column. */
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      height: '100vh',
      minWidth: '1200px',
      display: 'flex',
      flexDirection: 'column',
      background: 'var(--shell-ground)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement(TitleBar, {
    markSrc: "../../assets/icons/dragon-mark.svg"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      display: 'flex',
      minHeight: 0
    }
  }, /*#__PURE__*/React.createElement(Rail, {
    screen: screen,
    bucket: bucket,
    list: list,
    onScreen: s => {
      setScreen(s);
      setList(null);
    },
    onBucket: b => {
      setScreen('library');
      setBucket(b);
      setList(null);
    },
    onList: l => {
      setScreen('library');
      setList(l);
      setBucket(null);
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      display: 'flex',
      flexDirection: 'column',
      minWidth: 0,
      background: 'var(--wall-ground)',
      overflow: 'hidden'
    }
  }, screen === 'library' ? /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(CommandBar, {
    view: view,
    onView: setView,
    density: density,
    onDensity: setDensity,
    search: search,
    onSearch: setSearch,
    sort: sort,
    onSort: setSort,
    filtersOpen: filtersOpen,
    onToggleFilters: () => setFiltersOpen(!filtersOpen),
    ruleCount: rules.length,
    selectedCount: selected ? 1 : 0
  }), /*#__PURE__*/React.createElement(CutBar, {
    chips: chips,
    total: "1,247",
    result: games.length,
    onDrop: dropChip,
    onClear: () => {
      setRules([]);
      setBucket(null);
    }
  }), view === 'grid' ? /*#__PURE__*/React.createElement(CoverWall, {
    games: games,
    density: density,
    selected: selected,
    bucketLabel: b => BUCKET_LABEL[b],
    emptyMessage: EMPTY[bucket] || 'Nothing matches that cut. Drop a rule on the bar above.',
    onSelect: setSelected,
    onOpenDetails: setDetails
  }) : /*#__PURE__*/React.createElement(ListView, {
    games: games,
    selected: selected,
    sort: sort,
    onSort: setSort,
    onSelect: setSelected
  })) : screen === 'feed' ? /*#__PURE__*/React.createElement(FeedScreen, {
    onLaunch: launch
  }) : screen === 'merge' ? /*#__PURE__*/React.createElement(MergeQueue, null) : screen === 'stores' ? /*#__PURE__*/React.createElement(NotRecreated, {
    message: "The Stores panel is not recreated in this kit. In the app it lists what each platform contributes from local files, and what it can only know once you sign in."
  }) : /*#__PURE__*/React.createElement(NotRecreated, {
    message: "The Appearance screen is not recreated in this kit. In the app it holds the theme picker, the transparency slider and the flush/floating layout switch."
  })), screen === 'library' && filtersOpen ? /*#__PURE__*/React.createElement(FilterPanel, {
    rules: rules,
    onToggle: toggleRule,
    onClose: () => setFiltersOpen(false)
  }) : null), /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      left: '18px',
      bottom: '18px',
      display: 'flex',
      flexDirection: 'column',
      gap: '10px',
      zIndex: 35
    }
  }, journal ? /*#__PURE__*/React.createElement(DockCard, {
    title: "Vintage Story",
    meta: "1h 47m \xB7 ended 12:04 AM",
    onDismiss: () => setJournal(false)
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '10px'
    }
  }, /*#__PURE__*/React.createElement(RatingDots, {
    value: rating,
    onChange: setRating
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: '8px'
    }
  }, /*#__PURE__*/React.createElement(TextField, {
    on: "surface",
    placeholder: "How was it?"
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "primary",
    size: "sm",
    onClick: () => setJournal(false)
  }, "Save")))) : null, launching ? /*#__PURE__*/React.createElement(DockCard, null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '10px'
    }
  }, /*#__PURE__*/React.createElement(StatusPip, {
    state: "waiting"
  }), /*#__PURE__*/React.createElement("span", {
    className: "body",
    style: {
      fontSize: '12px'
    }
  }, "Starting ", launching, "\u2026"))) : null), /*#__PURE__*/React.createElement(DetailsModal, {
    game: details,
    bucketLabel: b => BUCKET_LABEL[b],
    onClose: () => setDetails(null),
    onLaunch: launch
  }));
}
ReactDOM.createRoot(document.getElementById('root')).render(/*#__PURE__*/React.createElement(App, null));
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/app.jsx", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/data.js
try { (() => {
/* Sample library. Procedural gradients stand in for cover art — real builds
   pull IGDB covers and Steam's library_600x900 portrait capsule, and a mock
   never reproduces publisher art. */
window.WINNOW_DATA = {
  total: '1,247',
  buckets: [{
    id: 'patched',
    label: 'Patched since',
    count: 94,
    pip: true
  }, {
    id: 'never',
    label: 'Never played',
    count: 412
  }, {
    id: 'bounced',
    label: 'Bounced off',
    count: 186
  }, {
    id: 'playedout',
    label: 'Played out',
    count: 391
  }, {
    id: 'wontrun',
    label: "Won't run",
    count: 0
  }],
  lists: [{
    id: 'couch',
    name: 'Couch co-op night',
    count: 4
  }, {
    id: 'finish',
    name: 'Finish these first',
    count: 5
  }],
  liveLists: [{
    id: 'coop',
    name: 'Co-op I bounced off',
    count: 136
  }, {
    id: 'unplayed',
    name: 'Unplayed adventures',
    count: 342
  }],
  games: [{
    t: 'RimWorld',
    s: 'STEAM',
    h: '312h',
    idle: 8,
    ago: '8mo',
    last: '4 Jan 2026',
    y: '2018',
    b: 'playedout',
    u: 3,
    g: ['#C2410C', '#7C2D12'],
    installed: true
  }, {
    t: 'Vintage Story',
    s: 'GOG',
    h: '41h',
    idle: 11,
    ago: '11mo',
    last: '2 Oct 2025',
    y: '2016',
    b: 'bounced',
    u: 3,
    g: ['#2E7D5B', '#8FBF3F'],
    installed: true
  }, {
    t: 'Factorio',
    s: 'STEAM',
    h: '190h',
    idle: 16,
    ago: '1y 4mo',
    last: '9 May 2025',
    y: '2020',
    b: 'playedout',
    u: 2,
    g: ['#D97706', '#78350F'],
    installed: true
  }, {
    t: 'Oxygen Not Included',
    s: 'STEAM',
    h: '52h',
    idle: 14,
    ago: '1y 2mo',
    last: '18 Jul 2025',
    y: '2019',
    b: 'bounced',
    u: 3,
    g: ['#0E7490', '#164E63'],
    installed: false
  }, {
    t: 'Deep Rock Galactic',
    s: 'STEAM',
    h: '127h',
    idle: 19,
    ago: '1y 7mo',
    last: '3 Feb 2025',
    y: '2020',
    b: 'playedout',
    u: 2,
    g: ['#B45309', '#451A03'],
    installed: true
  }, {
    t: 'Valheim',
    s: 'STEAM',
    h: '96h',
    idle: 21,
    ago: '1y 9mo',
    last: '11 Dec 2024',
    y: '2021',
    b: 'playedout',
    u: 2,
    g: ['#365314', '#1A2E05'],
    installed: false
  }, {
    t: 'Dyson Sphere Program',
    s: 'STEAM',
    h: '37h',
    idle: 24,
    ago: '2y',
    last: '20 Sep 2024',
    y: '2021',
    b: 'bounced',
    u: 2,
    g: ['#1E3A8A', '#4C1D95'],
    installed: false
  }, {
    t: 'Project Zomboid',
    s: 'STEAM',
    h: '88h',
    idle: 25,
    ago: '2y 1mo',
    last: '2 Aug 2024',
    y: '2013',
    b: 'bounced',
    u: 2,
    g: ['#7F1D1D', '#292524'],
    installed: true
  }, {
    t: 'Satisfactory',
    s: 'EPIC',
    h: '64h',
    idle: 29,
    ago: '2y 5mo',
    last: '14 Apr 2024',
    y: '2020',
    b: 'bounced',
    u: 2,
    g: ['#EA580C', '#9A3412'],
    installed: false
  }, {
    t: 'Terraria',
    s: 'STEAM',
    h: '204h',
    idle: 32,
    ago: '2y 8mo',
    last: '9 Jan 2024',
    y: '2011',
    b: 'playedout',
    u: 2,
    g: ['#0891B2', '#155E75'],
    installed: true
  }, {
    t: 'Kenshi',
    s: 'GOG',
    h: '23h',
    idle: 38,
    ago: '3y 2mo',
    last: '30 Jun 2023',
    y: '2018',
    b: 'bounced',
    u: 1,
    g: ['#78716C', '#44403C'],
    installed: false
  }, {
    t: 'Grim Dawn',
    s: 'GOG',
    h: '71h',
    idle: 42,
    ago: '3y 6mo',
    last: '2 Mar 2023',
    y: '2016',
    b: 'bounced',
    u: 1,
    g: ['#5B21B6', '#1E1B4B'],
    installed: false
  }, {
    t: 'Subnautica',
    s: 'STEAM',
    h: '44h',
    idle: 30,
    ago: '2y 6mo',
    last: '1 Mar 2024',
    y: '2018',
    b: 'bounced',
    u: 1,
    g: ['#0369A1', '#0C4A6E'],
    installed: true
  }, {
    t: 'Stardew Valley',
    s: 'GOG',
    h: '156h',
    idle: 18,
    ago: '1y 6mo',
    last: '6 Mar 2025',
    y: '2016',
    b: 'playedout',
    u: 2,
    g: ['#65A30D', '#3F6212'],
    installed: true
  }, {
    t: 'Slay the Spire',
    s: 'STEAM',
    h: '89h',
    idle: 27,
    ago: '2y 3mo',
    last: '7 Jun 2024',
    y: '2019',
    b: 'playedout',
    u: 1,
    g: ['#BE123C', '#4C0519'],
    installed: false
  }, {
    t: 'Noita',
    s: 'STEAM',
    h: '31h',
    idle: 35,
    ago: '2y 11mo',
    last: '2 Oct 2023',
    y: '2020',
    b: 'bounced',
    u: 1,
    g: ['#A16207', '#1C1917'],
    installed: false
  }, {
    t: 'Hollow Knight',
    s: 'STEAM',
    h: '62h',
    idle: 40,
    ago: '3y 4mo',
    last: '19 May 2023',
    y: '2017',
    b: 'bounced',
    u: 1,
    g: ['#1E293B', '#020617'],
    installed: true
  }, {
    t: 'Outer Wilds',
    s: 'EPIC',
    h: '22h',
    idle: 26,
    ago: '2y 2mo',
    last: '4 Jul 2024',
    y: '2019',
    b: 'bounced',
    u: 1,
    g: ['#7C3AED', '#312E81'],
    installed: false
  }, {
    t: 'Empyrion: Galactic Survival',
    s: 'STEAM',
    h: '37h',
    idle: 44,
    ago: '3y 8mo',
    last: '2 Jan 2023',
    y: '2020',
    b: 'patched',
    u: 2,
    g: ['#155E75', '#1E3A8A'],
    installed: false
  }, {
    t: 'Dwarf Fortress',
    s: 'STEAM',
    h: '0h',
    idle: 44,
    ago: 'never',
    last: null,
    y: '2022',
    b: 'never',
    u: 0,
    g: ['#57534E', '#292524'],
    installed: false
  }, {
    t: 'Caves of Qud',
    s: 'STEAM',
    h: '14m',
    idle: 33,
    ago: '2y 9mo',
    last: '8 Dec 2023',
    y: '2024',
    b: 'never',
    u: 0,
    g: ['#166534', '#052E16'],
    installed: true
  }, {
    t: 'Disco Elysium',
    s: 'GOG',
    h: '0h',
    idle: 48,
    ago: 'never',
    last: null,
    y: '2019',
    b: 'never',
    u: 0,
    g: ['#7E22CE', '#3B0764'],
    installed: false
  }],
  shelves: [{
    id: 'patched_while_away',
    title: 'Patched while you were away',
    blurb: "Major updates landed after you stopped playing — the game you left isn't the game that's waiting.",
    cards: [{
      t: 'Empyrion: Galactic Survival',
      reason: 'You put 37 hours into this in 2023 and it has had 2 updates since, most recently "v1.19.2 Patch".'
    }, {
      t: 'Vintage Story',
      reason: 'You put 41 hours into this in 2025 and it has had 3 updates since, most recently "Stable v1.20.4".'
    }]
  }, {
    id: 'worth_another_look',
    title: 'Worth another look',
    blurb: 'You committed real hours past the refund line, then drifted off mid-story.',
    cards: [{
      t: 'Outer Wilds',
      reason: 'You put 22 hours in — that was 2024. It is installed, so there is nothing between you and finding out.'
    }, {
      t: 'Kenshi',
      reason: 'You put 23 hours in — that was 2023.'
    }]
  }, {
    id: 'ready_to_play',
    title: 'Installed and waiting',
    blurb: 'Already on your disk with nothing sunk — zero friction between you and finding out.',
    cards: [{
      t: 'Caves of Qud',
      reason: 'You tried it for 14 minutes — that was 2023. It is installed.'
    }]
  }, {
    id: 'barely_touched',
    title: 'Barely gave it a chance',
    blurb: 'Under 2 hours in — you opened the door and never walked through.',
    cards: [{
      t: 'Dyson Sphere Program',
      reason: 'You opened this once in 2024 and it has had 2 updates since.'
    }]
  }, {
    id: 'on_your_taste',
    title: 'Never opened, right up your alley',
    blurb: 'Sitting sealed in your library, and it matches where your hours actually go.',
    cards: [{
      t: 'Dwarf Fortress',
      reason: "Never opened. Your hours concentrate in colony sim, and this is the one that started it."
    }, {
      t: 'Disco Elysium',
      reason: 'Never opened. You bought it twice, on two stores.'
    }]
  }],
  facets: [{
    group: 'GENRE',
    options: [['Simulation', 214], ['RPG', 186], ['Strategy', 141], ['Survival', 97], ['Roguelike', 62], ['Racing', 0]]
  }, {
    group: 'GAME MODE',
    options: [['Single player', 806], ['Co-op', 231], ['Multiplayer', 174]]
  }, {
    group: 'ON DISK',
    options: [['Installed', 148], ['Not installed', 778]]
  }],
  sorts: [{
    id: 'dormant',
    label: 'Dormant longest'
  }, {
    id: 'playtime',
    label: 'Playtime'
  }, {
    id: 'title',
    label: 'Title'
  }, {
    id: 'recent',
    label: 'Recently played'
  }, {
    id: 'added',
    label: 'Recently added'
  }]
};
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/data.js", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/details.jsx
try { (() => {
const {
  GapRail,
  Badge,
  Button
} = window.WinnowDesignSystem_df253a;

/* The detail modal (§10): four bands — what is this, my history, get me in,
   the rest. It stays a modal over the library so Escape returns the user to
   exactly the row they were reading. */
function DetailsModal(props) {
  const g = props.game;
  if (!g) return null;
  const never = !g.last;
  return /*#__PURE__*/React.createElement("div", {
    onClick: props.onClose,
    style: {
      position: 'absolute',
      inset: 0,
      zIndex: 40,
      background: 'var(--modal-scrim)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      padding: '40px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    onClick: e => e.stopPropagation(),
    style: {
      display: 'grid',
      gridTemplateColumns: '200px 1fr',
      columnGap: '26px',
      width: '820px',
      maxHeight: '100%',
      padding: '24px 26px 0',
      background: 'var(--surface)',
      border: '1px solid var(--line)',
      borderRadius: 'var(--radius-tile)',
      boxShadow: 'var(--shadow-modal)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '18px',
      paddingBottom: '24px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      height: '300px',
      borderRadius: 'var(--radius-tile)',
      border: '1px solid var(--line-soft)',
      background: grad(g.g),
      display: 'flex',
      alignItems: 'flex-end',
      padding: '16px'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--type-heading)',
      fontWeight: 700,
      fontSize: '22px',
      lineHeight: '24px',
      color: '#fff',
      textShadow: '0 2px 14px rgba(0,0,0,.65)'
    }
  }, g.t)), /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "label",
    style: {
      fontSize: '10px'
    }
  }, "Steam appid"), /*#__PURE__*/React.createElement("div", {
    className: "data",
    style: {
      marginTop: '3px'
    }
  }, "383120")), g.installed ? /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "label",
    style: {
      fontSize: '10px'
    }
  }, "On disk"), /*#__PURE__*/React.createElement("div", {
    className: "data-s",
    style: {
      marginTop: '3px',
      lineHeight: '15px',
      wordBreak: 'break-all'
    }
  }, "D:\\\\SteamLibrary\\\\steamapps\\\\common\\\\", g.t.split(':')[0])) : null), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      minWidth: 0,
      paddingBottom: '24px',
      overflowY: 'auto'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'flex-start',
      gap: '12px'
    }
  }, /*#__PURE__*/React.createElement("h2", {
    className: "display-l",
    style: {
      margin: 0,
      fontSize: '26px',
      lineHeight: '30px',
      flex: 1
    }
  }, g.t), /*#__PURE__*/React.createElement(Button, {
    variant: "ctl",
    onClick: props.onClose,
    tooltip: "Close (Esc)"
  }, "\u2715")), /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: '6px'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "data",
    style: {
      fontSize: '12px',
      color: 'var(--text-dim)'
    }
  }, g.y)), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: '8px',
      marginTop: '14px'
    }
  }, /*#__PURE__*/React.createElement(Badge, null, g.s), /*#__PURE__*/React.createElement(Badge, {
    variant: "fill"
  }, props.bucketLabel(g.b)), /*#__PURE__*/React.createElement(Badge, {
    variant: "fill"
  }, g.installed ? 'Installed' : 'Not installed')), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'auto 1fr',
      columnGap: '26px',
      marginTop: '18px'
    }
  }, /*#__PURE__*/React.createElement("div", null, /*#__PURE__*/React.createElement("div", {
    className: "data",
    style: {
      fontSize: '30px',
      lineHeight: '34px'
    }
  }, g.h), /*#__PURE__*/React.createElement("div", {
    className: "label",
    style: {
      fontSize: '10px'
    }
  }, "Played")), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '7px'
    }
  }, never ? /*#__PURE__*/React.createElement("p", {
    className: "body",
    style: {
      margin: 0,
      color: 'var(--text-dim)'
    }
  }, "You've never opened this.") : /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'space-between',
      alignItems: 'baseline'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "label",
    style: {
      fontSize: '10px'
    }
  }, "Since you played"), /*#__PURE__*/React.createElement("span", {
    className: "data",
    style: {
      fontSize: '14px'
    }
  }, g.ago)), /*#__PURE__*/React.createElement(GapRail, {
    marks: g.u ? [0.58, 0.86, 0.94].slice(0, g.u) : [],
    markTooltip: "An update landed here"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'space-between'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "data-s"
  }, g.last), /*#__PURE__*/React.createElement("span", {
    className: "data-s"
  }, "today")), /*#__PURE__*/React.createElement("p", {
    className: "body",
    style: {
      margin: 0,
      fontSize: '12px',
      lineHeight: '17px',
      color: 'var(--text-dim)'
    }
  }, g.u ? g.u + ' updates landed while you were away.' : 'No updates recorded in that stretch.', " Checked once, on 23 Aug 2026.")))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: '10px',
      marginTop: '18px',
      flexWrap: 'wrap'
    }
  }, /*#__PURE__*/React.createElement(Button, {
    variant: "primary",
    onClick: () => props.onLaunch(g.t)
  }, g.installed ? 'Play' : 'Install'), /*#__PURE__*/React.createElement(Button, {
    variant: "link"
  }, "Store page"), /*#__PURE__*/React.createElement(Button, {
    variant: "link"
  }, "All patch notes"), g.installed ? /*#__PURE__*/React.createElement(Button, {
    variant: "link"
  }, "Open folder") : null), /*#__PURE__*/React.createElement("div", {
    style: {
      height: '1px',
      background: 'var(--line)',
      margin: '22px 0 18px'
    }
  }), /*#__PURE__*/React.createElement("div", {
    className: "label"
  }, "About"), /*#__PURE__*/React.createElement("p", {
    className: "body",
    style: {
      margin: '7px 0 0',
      color: 'var(--text-dim)',
      lineHeight: '20px'
    }
  }, "No description yet. Winnow fills the year, publisher and summary in from IGDB as it works through your library."), g.u ? /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: '22px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "label"
  }, "Since you played"), [['v1.19.2 Patch', '11 Aug 2026'], ['Hotfix 1.19.1', '2 Jun 2026'], ['Alpha 12 — Story mode', '14 Nov 2025']].slice(0, g.u + 1).map((u, i) => /*#__PURE__*/React.createElement("div", {
    key: u[0],
    style: {
      display: 'grid',
      gridTemplateColumns: '16px 1fr auto auto',
      gap: '10px',
      alignItems: 'center',
      padding: '9px 0',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: '7px',
      height: '7px',
      borderRadius: '50%',
      background: i < g.u ? 'var(--flare)' : 'transparent',
      boxShadow: i < g.u ? 'var(--pip-glow)' : 'none'
    }
  }), /*#__PURE__*/React.createElement("span", {
    className: "body"
  }, u[0]), /*#__PURE__*/React.createElement("span", {
    className: "data-s"
  }, u[1]), /*#__PURE__*/React.createElement("a", {
    className: "body",
    style: {
      fontSize: '12px'
    },
    href: "#"
  }, "Patch notes")))) : null)));
}
Object.assign(window, {
  DetailsModal
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/details.jsx", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/feed.jsx
try { (() => {
const {
  SectionPanel,
  FeedCard,
  Button
} = window.WinnowDesignSystem_df253a;

/* The feed: five sections in wrapping grids, each a different query over one
   scoring pass. Every card carries a sentence, and the sentence is the
   product. */
function FeedScreen(props) {
  const d = window.WINNOW_DATA;
  const [answers, setAnswers] = React.useState({});
  const byTitle = {};
  d.games.forEach(g => {
    byTitle[g.t] = g;
  });
  const answer = (title, note) => setAnswers(Object.assign({}, answers, {
    [title]: note
  }));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      display: 'flex',
      flexDirection: 'column',
      background: 'var(--pane-ground)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 'none',
      padding: '20px 24px 18px',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("h1", {
    className: "display-l",
    style: {
      margin: 0
    }
  }, "The feed"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '6px',
      marginTop: '7px'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "data"
  }, "926"), /*#__PURE__*/React.createElement("span", {
    className: "body",
    style: {
      color: 'var(--text-dim)'
    }
  }, "games scored")), /*#__PURE__*/React.createElement("p", {
    className: "body",
    style: {
      margin: '7px 0 0',
      maxWidth: '640px',
      fontSize: '12px',
      lineHeight: '18px',
      color: 'var(--text-dim)'
    }
  }, "Ranked on your own history \u2014 hours, dormancy and what has shipped since you stopped. Winnow has one playtime reading for most of your library, so the reasons cite what it watched happen, not totals it inherited.")), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: 'auto',
      padding: '24px 24px 32px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: 'var(--gap-section)'
    }
  }, d.shelves.map(s => /*#__PURE__*/React.createElement(SectionPanel, {
    key: s.id,
    title: s.title,
    count: s.cards.length,
    blurb: s.blurb
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(auto-fill,minmax(420px,1fr))',
      gap: 'var(--gap-card)'
    }
  }, s.cards.map(c => {
    const g = byTitle[c.t] || {};
    return /*#__PURE__*/React.createElement(FeedCard, {
      key: c.t,
      title: c.t,
      reason: c.reason,
      store: g.s,
      monthsIdle: g.idle,
      gradient: g.g ? grad(g.g) : undefined,
      unread: s.id === 'patched_while_away',
      unreadTooltip: (g.u || 2) + ' updates since you played',
      primaryLabel: g.installed ? 'Play' : 'Install',
      setAsideNote: answers[c.t],
      setAsideDate: answers[c.t] ? '2 Sep 2026' : undefined,
      onNotInterested: () => answer(c.t, 'Not interested, set'),
      onNotNow: () => answer(c.t, 'Not now, until'),
      onUndo: () => answer(c.t, undefined),
      onPrimary: () => props.onLaunch(c.t)
    });
  })))))));
}
Object.assign(window, {
  FeedScreen
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/feed.jsx", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/filters.jsx
try { (() => {
const {
  Checkbox,
  TextField,
  Button
} = window.WinnowDesignSystem_df253a;

/* A 276px column to the RIGHT of the grid, on the rail's own Surface — a
   peer of the rail rather than a second column of it. Its header is 48px so
   the rule under FILTERS continues the command bar's rule across the
   window. The grid narrows rather than being covered, which is the only way
   the counts pay for themselves: their whole value is watching them move. */
function FilterPanel(props) {
  const d = window.WINNOW_DATA;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      width: 'var(--filter-panel-width)',
      flex: 'none',
      background: 'var(--chrome-surface)',
      borderLeft: '1px solid var(--line)',
      display: 'flex',
      flexDirection: 'column',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      height: 'var(--command-bar-height)',
      flex: 'none',
      display: 'flex',
      alignItems: 'center',
      gap: '10px',
      padding: '0 16px',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "label",
    style: {
      fontSize: '10px',
      letterSpacing: '1.4px'
    }
  }, "Filters"), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "ctl",
    onClick: props.onClose
  }, "\u2715")), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: 'auto',
      padding: '16px'
    }
  }, d.facets.map(f => /*#__PURE__*/React.createElement("div", {
    key: f.group,
    style: {
      marginBottom: '20px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "label",
    style: {
      fontSize: '10px',
      marginBottom: '8px'
    }
  }, f.group), f.group === 'GENRE' ? /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: '8px'
    }
  }, /*#__PURE__*/React.createElement(TextField, {
    on: "surface",
    placeholder: "Find a genre"
  })) : null, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: '2px'
    }
  }, f.options.map(([label, count]) => /*#__PURE__*/React.createElement(Checkbox, {
    key: label,
    label: label,
    count: count,
    checked: props.rules.indexOf(label) >= 0,
    onChange: () => props.onToggle(label)
  }))))), /*#__PURE__*/React.createElement("div", {
    style: {
      marginBottom: '8px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    className: "label",
    style: {
      fontSize: '10px',
      marginBottom: '8px'
    }
  }, "Release year"), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '8px'
    }
  }, /*#__PURE__*/React.createElement(TextField, {
    on: "surface",
    placeholder: "1993",
    width: "76px"
  }), /*#__PURE__*/React.createElement("span", {
    className: "data-s"
  }, "to"), /*#__PURE__*/React.createElement(TextField, {
    on: "surface",
    placeholder: "2026",
    width: "76px"
  })))));
}
Object.assign(window, {
  FilterPanel
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/filters.jsx", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/library.jsx
try { (() => {
const {
  GameTile,
  LibraryRow,
  EmptyState,
  Button
} = window.WinnowDesignSystem_df253a;
function grad(g) {
  return 'linear-gradient(155deg,' + g[0] + ',' + g[1] + ')';
}

/* The cover wall. The grid reflows on available width — never a fixed
   column count — and the tiles are the only thing on screen with light in
   them. */
function CoverWall(props) {
  if (!props.games.length) {
    return /*#__PURE__*/React.createElement(EmptyState, {
      message: props.emptyMessage
    });
  }
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: 'auto',
      padding: '20px 24px 20px 20px'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: 'repeat(auto-fill,minmax(' + props.density + 'px,1fr))',
      gap: 'var(--gap-tile)',
      alignContent: 'start'
    }
  }, props.games.map(g => /*#__PURE__*/React.createElement(GameTile, {
    key: g.t,
    title: g.t,
    gradient: grad(g.g),
    monthsIdle: g.idle,
    width: "100%",
    playtime: g.h,
    idle: g.ago,
    lastPlayed: g.last || 'never',
    store: g.s,
    releaseYear: g.y,
    bucketLabel: props.bucketLabel(g.b),
    unread: g.u > 0 && g.b === 'patched',
    unreadTooltip: g.u + ' updates since you played',
    selected: props.selected === g.t,
    primaryLabel: g.installed ? 'Play' : 'Install',
    primaryHint: g.installed ? 'Launch through ' + g.s : 'Install through ' + g.s,
    onOpenDetails: () => props.onOpenDetails(g),
    onAddToList: () => props.onSelect(g.t)
  }))));
}

/* List view: same data, no art dependency. This is the power-user view —
   sortable columns, multi-select — which is how the analytics capability
   stays available without dominating the default experience. */
function ListView(props) {
  const head = (label, align, active) => /*#__PURE__*/React.createElement("span", {
    className: "label",
    style: {
      fontSize: '10px',
      textAlign: align || 'left',
      display: 'flex',
      alignItems: 'center',
      gap: '6px',
      justifyContent: align === 'right' ? 'flex-end' : 'flex-start',
      cursor: 'pointer',
      color: active ? 'var(--text)' : 'var(--text-dim)'
    }
  }, align === 'right' && active ? /*#__PURE__*/React.createElement("i", {
    style: {
      borderLeft: '4px solid transparent',
      borderRight: '4px solid transparent',
      borderTop: '5px solid var(--volt)'
    }
  }) : null, label, align !== 'right' && active ? /*#__PURE__*/React.createElement("i", {
    style: {
      borderLeft: '4px solid transparent',
      borderRight: '4px solid transparent',
      borderTop: '5px solid var(--volt)'
    }
  }) : null);
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      display: 'flex',
      flexDirection: 'column',
      background: 'var(--pane-ground)',
      overflow: 'hidden'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '2px 18px 1fr 84px 104px 92px 24px',
      alignItems: 'center',
      height: '30px',
      flex: 'none',
      background: 'var(--chrome-surface)',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("i", null), /*#__PURE__*/React.createElement("i", null), /*#__PURE__*/React.createElement("span", {
    onClick: () => props.onSort('title')
  }, head('Title', 'left', props.sort === 'title')), /*#__PURE__*/React.createElement("span", {
    className: "label",
    style: {
      fontSize: '10px'
    }
  }, "Store"), /*#__PURE__*/React.createElement("span", {
    onClick: () => props.onSort('playtime')
  }, head('Playtime', 'right', props.sort === 'playtime')), /*#__PURE__*/React.createElement("span", {
    onClick: () => props.onSort('dormant')
  }, head('Idle', 'right', props.sort === 'dormant')), /*#__PURE__*/React.createElement("i", null)), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: 'auto'
    }
  }, props.games.map(g => /*#__PURE__*/React.createElement(LibraryRow, {
    key: g.t,
    title: g.t,
    store: g.s,
    playtime: g.h,
    idle: g.ago,
    unread: g.u > 0 && g.b === 'patched',
    selected: props.selected === g.t,
    onClick: () => props.onSelect(g.t)
  }))));
}
Object.assign(window, {
  CoverWall,
  ListView,
  grad
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/library.jsx", error: String((e && e.message) || e) }); }

// ui_kits/desktop-app/shell.jsx
try { (() => {
const {
  TitleBar,
  RailRow,
  SegmentedToggle,
  SortMenu,
  Button,
  CountPill,
  TextField,
  DensitySlider,
  CutChip
} = window.WinnowDesignSystem_df253a;

/* The 220px rail: screens above the cuts, cuts above the lists. Element
   order is reading order is Tab order. */
function Rail(props) {
  const d = window.WINNOW_DATA;
  const heading = text => /*#__PURE__*/React.createElement("div", {
    style: {
      margin: '22px 16px 8px',
      paddingTop: '14px',
      borderTop: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: "label",
    style: {
      fontSize: '10px',
      letterSpacing: '1.4px'
    }
  }, text));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      width: 'var(--rail-width)',
      flex: 'none',
      background: 'var(--chrome-surface)',
      borderRight: '1px solid var(--line)',
      overflowY: 'auto',
      padding: '20px 0'
    }
  }, /*#__PURE__*/React.createElement(RailRow, {
    label: "The feed",
    state: props.screen === 'feed' ? 'selected' : 'default',
    tooltip: "Games worth going back to, and why",
    onClick: () => props.onScreen('feed')
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      margin: '12px 16px',
      borderTop: '1px solid var(--line)'
    }
  }), /*#__PURE__*/React.createElement(RailRow, {
    label: "All games",
    count: d.total,
    tooltip: "Every title you own",
    state: props.screen === 'library' && !props.bucket ? 'selected' : 'default',
    onClick: () => props.onBucket(null)
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      margin: '10px 16px',
      height: '1px',
      background: 'var(--line)'
    }
  }), d.buckets.map(b => /*#__PURE__*/React.createElement(RailRow, {
    key: b.id,
    label: b.label,
    count: b.count,
    pip: b.pip,
    dim: b.count === 0,
    state: props.screen === 'library' && props.bucket === b.id ? 'selected' : 'default',
    onClick: () => props.onBucket(b.id)
  })), heading('REVIEW'), /*#__PURE__*/React.createElement(RailRow, {
    label: "Same game?",
    count: 7,
    tooltip: "Pairs that might be the same game",
    state: props.screen === 'merge' ? 'selected' : 'default',
    onClick: () => props.onScreen('merge')
  }), heading('SETTINGS'), /*#__PURE__*/React.createElement(RailRow, {
    label: "Stores",
    state: props.screen === 'stores' ? 'selected' : 'default',
    onClick: () => props.onScreen('stores')
  }), /*#__PURE__*/React.createElement(RailRow, {
    label: "Appearance",
    state: props.screen === 'appearance' ? 'selected' : 'default',
    onClick: () => props.onScreen('appearance')
  }), heading('LISTS'), d.lists.map(l => /*#__PURE__*/React.createElement(RailRow, {
    key: l.id,
    kind: "list",
    label: l.name,
    count: l.count,
    state: props.list === l.id ? 'selected' : 'default',
    onClick: () => props.onList(l.id)
  })), heading('LIVE LISTS'), d.liveLists.map(l => /*#__PURE__*/React.createElement(RailRow, {
    key: l.id,
    kind: "list",
    label: l.name,
    count: l.count,
    tooltip: "A live list: it holds a rule and finds its own members",
    state: props.list === l.id ? 'selected' : 'default',
    onClick: () => props.onList(l.id)
  })));
}

/* The command bar lives INSIDE the library pane, so the other screens can
   never appear under library-only controls. Search takes the slack;
   everything else is Auto. */
function CommandBar(props) {
  const d = window.WINNOW_DATA;
  const [sortOpen, setSortOpen] = React.useState(false);
  const sortLabel = (d.sorts.find(s => s.id === props.sort) || d.sorts[0]).label;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      height: 'var(--command-bar-height)',
      flex: 'none',
      display: 'flex',
      alignItems: 'center',
      gap: '10px',
      padding: '0 20px',
      borderBottom: '1px solid var(--line)'
    }
  }, /*#__PURE__*/React.createElement(TextField, {
    value: props.search,
    onChange: props.onSearch,
    placeholder: 'Search ' + d.total + ' titles…',
    width: "240px"
  }), /*#__PURE__*/React.createElement(SegmentedToggle, {
    value: props.view,
    onChange: props.onView
  }), /*#__PURE__*/React.createElement(DensitySlider, {
    label: "Density",
    value: props.density,
    onChange: props.onDensity
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      width: '1px',
      height: '18px',
      background: 'var(--line)'
    }
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "ctl"
  }, "Display"), props.selectedCount > 1 ? /*#__PURE__*/React.createElement("span", {
    className: "data",
    style: {
      fontSize: '11px',
      color: 'var(--volt)'
    }
  }, props.selectedCount, " selected") : null, /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement(Button, {
    variant: "ctl",
    active: sortOpen,
    onClick: () => setSortOpen(!sortOpen),
    tooltip: "Sort order"
  }, sortLabel, " \u25BE"), /*#__PURE__*/React.createElement(Button, {
    variant: "ctl",
    active: props.filtersOpen,
    onClick: props.onToggleFilters,
    tooltip: "Filter by genre, tag, store and more"
  }, "Filters ", props.ruleCount ? /*#__PURE__*/React.createElement(CountPill, null, props.ruleCount) : null), sortOpen ? /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      top: '44px',
      right: '150px',
      zIndex: 30
    }
  }, /*#__PURE__*/React.createElement(SortMenu, {
    value: props.sort,
    options: d.sorts,
    onChange: id => {
      props.onSort(id);
      setSortOpen(false);
    }
  })) : null);
}

/* A library that has been cut down and does not say so is the most
   expensive confusion this screen can produce. */
function CutBar(props) {
  if (!props.chips.length) return null;
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      flexWrap: 'wrap',
      padding: '10px 20px',
      borderBottom: '1px solid var(--line)'
    }
  }, props.chips.map(c => /*#__PURE__*/React.createElement(CutChip, {
    key: c.id,
    kind: c.kind,
    label: c.label,
    tooltip: c.tooltip,
    onDismiss: () => props.onDrop(c.id)
  })), /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), /*#__PURE__*/React.createElement("span", {
    className: "data",
    style: {
      color: 'var(--text-dim)'
    }
  }, props.total, " \u2192 ", /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--volt)'
    }
  }, props.result)), /*#__PURE__*/React.createElement(Button, {
    variant: "ctl",
    onClick: props.onClear
  }, "Clear filters"));
}
Object.assign(window, {
  Rail,
  CommandBar,
  CutBar
});
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/desktop-app/shell.jsx", error: String((e && e.message) || e) }); }

__ds_ns.Badge = __ds_scope.Badge;

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Checkbox = __ds_scope.Checkbox;

__ds_ns.CountPill = __ds_scope.CountPill;

__ds_ns.DensitySlider = __ds_scope.DensitySlider;

__ds_ns.TextField = __ds_scope.TextField;

__ds_ns.UnreadDot = __ds_scope.UnreadDot;

__ds_ns.CutChip = __ds_scope.CutChip;

__ds_ns.DockCard = __ds_scope.DockCard;

__ds_ns.EmptyState = __ds_scope.EmptyState;

__ds_ns.RatingDots = __ds_scope.RatingDots;

__ds_ns.StatusPip = __ds_scope.StatusPip;

__ds_ns.FeedCard = __ds_scope.FeedCard;

__ds_ns.GameTile = __ds_scope.GameTile;

__ds_ns.GapRail = __ds_scope.GapRail;

__ds_ns.LibraryRow = __ds_scope.LibraryRow;

__ds_ns.SectionPanel = __ds_scope.SectionPanel;

__ds_ns.RailRow = __ds_scope.RailRow;

__ds_ns.SegmentedToggle = __ds_scope.SegmentedToggle;

__ds_ns.SortMenu = __ds_scope.SortMenu;

__ds_ns.TitleBar = __ds_scope.TitleBar;

})();
