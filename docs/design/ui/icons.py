"""Own Space UI icons. Own artwork, CC0. 48x48 grid, stroke 3, round caps and joins, one colour (currentColor).
The panel is text-first: only these eleven glyphs exist, where words do not work."""

S = 'fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"'

ICONS = {
    "undo": '<path d="M16 14L8 22l8 8"/><path d="M8 22h22a10 10 0 0 1 0 20H20"/>',
    "redo": '<path d="M32 14l8 8-8 8"/><path d="M40 22H18a10 10 0 0 0 0 20h10"/>',
    "chevron-left": '<path d="M30 8L14 24l16 16"/>',
    "chevron-right": '<path d="M18 8l16 16-16 16"/>',
    "settings": '<path d="M20 6h8l1.5 5a13 13 0 0 1 3.4 2l5-1.4 4 7-3.8 3.6a13 13 0 0 1 0 4l3.8 3.6-4 7-5-1.4a13 13 0 0 1-3.4 2L28 42h-8l-1.5-5a13 13 0 0 1-3.4-2l-5 1.4-4-7 3.8-3.6a13 13 0 0 1 0-4L6.1 18.2l4-7 5 1.4a13 13 0 0 1 3.4-2z"/><circle cx="24" cy="24" r="5.5"/>',
    "eye": '<path d="M4 24c4-8 10-12 20-12s16 4 20 12c-4 8-10 12-20 12S8 32 4 24z"/><circle cx="24" cy="24" r="6"/>',
    "sparkle": '<path d="M22 6l4 12 12 4-12 4-4 12-4-12-12-4 12-4z"/><path d="M38 30l1.6 4.4L44 36l-4.4 1.6L38 42l-1.6-4.4L32 36l4.4-1.6z" stroke-width="2.4"/>',
    "mic": '<rect x="17" y="5" width="14" height="24" rx="7"/><path d="M10 22a14 14 0 0 0 28 0M24 36v7M17 43h14"/>',
    "info": '<circle cx="24" cy="24" r="18"/><path d="M24 22v12"/><circle cx="24" cy="15" r="1.8" fill="currentColor"/>',
    "check": '<path d="M9 25l10 10L39 13"/>',
    "minimize": '<path d="M10 24h28"/>',
}
