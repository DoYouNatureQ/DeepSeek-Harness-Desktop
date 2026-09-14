/** Host half of the desktop tools plugin: the feature lives entirely in the
 * browser bundle; this half only exists so the Loader row resolves. */
export const name = 'dsh-desktop-tools'

/** No host-side services are required. */
export function apply() {}
