/**
 * Design System Configuration for Cost Analysis Automation
 *
 * Color Palette:
 * - Primary: Black (oklch(0.15 0 0)) - Main brand color for primary actions
 * - Secondary: Red (oklch(0.55 0.2 25)) - Accent color for highlights and alerts
 * - Neutrals: White, Grays, Off-whites - Background and text colors
 *
 * Typography:
 * - Font Family: Geist (sans-serif) for all text
 * - Headings: Bold weights (600-700)
 * - Body: Regular weight (400)
 *
 * Spacing: Uses Tailwind's default spacing scale (4px base unit)
 * Border Radius: 0.5rem (8px) default
 */

export const designSystem = {
  colors: {
    primary: "oklch(0.15 0 0)", // Black
    secondary: "oklch(0.55 0.2 25)", // Red
    background: "oklch(0.98 0 0)", // Off-white
    foreground: "oklch(0.15 0 0)", // Black
    muted: "oklch(0.92 0 0)", // Light gray
    border: "oklch(0.88 0 0)", // Border gray
  },
  typography: {
    fontFamily: {
      sans: '"Geist", "Geist Fallback", sans-serif',
      mono: '"Geist Mono", "Geist Mono Fallback", monospace',
    },
    fontSize: {
      xs: "0.75rem",
      sm: "0.875rem",
      base: "1rem",
      lg: "1.125rem",
      xl: "1.25rem",
      "2xl": "1.5rem",
      "3xl": "1.875rem",
      "4xl": "2.25rem",
    },
    fontWeight: {
      normal: 400,
      medium: 500,
      semibold: 600,
      bold: 700,
    },
  },
  spacing: {
    xs: "0.25rem",
    sm: "0.5rem",
    md: "1rem",
    lg: "1.5rem",
    xl: "2rem",
    "2xl": "3rem",
  },
  borderRadius: {
    sm: "calc(0.5rem - 2px)",
    md: "0.5rem",
    lg: "calc(0.5rem + 4px)",
  },
  shadows: {
    sm: "0 1px 2px 0 rgba(0, 0, 0, 0.05)",
    md: "0 4px 6px -1px rgba(0, 0, 0, 0.1)",
    lg: "0 10px 15px -3px rgba(0, 0, 0, 0.1)",
  },
} as const

export type DesignSystem = typeof designSystem
