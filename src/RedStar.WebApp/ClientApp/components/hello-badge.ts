import { LitElement, html } from 'lit';
import { customElement, property } from 'lit/decorators.js';

/**
 * Renders into light DOM, not shadow DOM, so the page's global Tailwind utility classes apply
 * inside this component's template -- a project-wide convention for RedStar.WebApp's components,
 * not a one-off choice. See RedStar.WebApp/CLAUDE.md's "Why Tailwind CSS v4" section for the full
 * reasoning and its trade-offs (this also means Lit's `static styles` scoped-CSS feature does not
 * apply here -- see the same section).
 */
@customElement('hello-badge')
export class HelloBadge extends LitElement {
  @property() label = '';

  protected createRenderRoot() {
    return this;
  }

  render() {
    return html`<span
      class="inline-block rounded-full bg-indigo-600 px-3 py-1 text-sm font-medium text-white"
    >
      Hello from Lit, ${this.label}!
    </span>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'hello-badge': HelloBadge;
  }
}
