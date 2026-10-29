---
description: Enterprise SSO OAuth UI/UX standards, brand vector asset invariants, and accessible tactile button design
---

# Enterprise SSO & Authentication UI/UX Standards

## 1. High-Fidelity Brand Vectors
- SSO authentication buttons (e.g., Google Workspace, Microsoft 365) must ALWAYS feature authentic multi-color SVG brand marks (Google 4-color 'G', Microsoft 4-square tiles) rather than text-only or monochromatic approximations.
- Vectors must use exact official brand color hex values and maintain appropriate aspect ratio dimensions (`w-4 h-4` or `w-4.5 h-4.5`).

## 2. Ergonomic Sizing & Accessibility
- Minimum touch target height for primary authentication mechanisms must be at least `40px` (`h-10`, `rounded-lg`).
- Avoid diminutive `h-8` heights on sign-in and authentication action buttons.

## 3. High-Contrast Typography
- Never style SSO provider labels with `text-muted-foreground`. Use high-contrast font styling (`text-foreground/90 font-medium text-sm`).

## 4. Tactile Micro-Interactions
- Implement micro-interactions on button interaction:
  - `active:scale-[0.98] transition-all duration-150`
  - Subtle brand-colored hover highlights (e.g., `hover:border-blue-500/40` for Google, `hover:border-sky-500/40` for Microsoft).

## 5. In-Flight Action Feedback
- Display an animated spinner (`Loader2 className="animate-spin"`) and dynamic copy (e.g., `Connecting to Google Workspace...`) during the asynchronous PKCE authorization redirect sequence.
