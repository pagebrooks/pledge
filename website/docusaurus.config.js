// @ts-check
const repository = 'https://github.com/pagebrooks/pledge';

// A quiet syntax theme: keywords, strings and comments get a color and everything else stays in the text color.
// The colors are CSS variables from custom.css, so one theme serves light and dark mode and matches the explainer.
const codeTheme = {
  plain: { color: 'var(--site-code-ink)', backgroundColor: 'var(--site-code-background)' },
  styles: [
    { types: ['comment', 'prolog', 'doctype', 'cdata'], style: { color: 'var(--site-code-comment)', fontStyle: 'italic' } },
    { types: ['keyword', 'tag'], style: { color: 'var(--site-code-keyword)' } },
    { types: ['string', 'char', 'attr-value'], style: { color: 'var(--site-code-string)' } },
    { types: ['attr-name'], style: { color: 'var(--site-code-keyword)' } },
  ],
};

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Pledge',
  tagline: 'Design by Contract for .NET',
  favicon: 'img/logo.svg',
  url: 'https://pledge.pagebrooks.com',
  baseUrl: '/',
  organizationName: 'pagebrooks',
  projectName: 'pledge',
  trailingSlash: false,

  // A broken link fails the build, so CI catches it before a release publishes the site.
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  markdown: {
    // .md files are plain CommonMark, so the rule pages in docs/rules need no MDX escaping.
    format: 'detect',
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          routeBasePath: '/',
          sidebarPath: './sidebars.js',
          editUrl: ({ docPath }) => `${repository}/edit/main/website/docs/${docPath}`,
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      }),
    ],
  ],

  plugins: [
    [
      '@docusaurus/plugin-content-docs',
      {
        // The rule pages the diagnostics link to live in docs/rules beside the code; the site renders them in place.
        id: 'rules',
        path: '../docs/rules',
        routeBasePath: 'rules',
        sidebarPath: './sidebars-rules.js',
        editUrl: ({ docPath }) => `${repository}/edit/main/docs/rules/${docPath}`,
      },
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      // Follow the OS light/dark setting until the reader picks one with the toggle.
      colorMode: {
        respectPrefersColorScheme: true,
      },
      navbar: {
        title: 'Pledge',
        logo: { alt: '', src: 'img/logo.svg' },
        items: [
          { type: 'docSidebar', sidebarId: 'docs', label: 'Docs', position: 'left' },
          { type: 'docSidebar', sidebarId: 'rules', docsPluginId: 'rules', label: 'Diagnostics', position: 'left' },
          { href: 'https://www.nuget.org/packages/Pledge', label: 'NuGet', position: 'right' },
          { href: 'https://github.com/sponsors/pagebrooks', label: 'Sponsor', position: 'right' },
          { href: repository, label: 'GitHub', position: 'right' },
        ],
      },
      footer: {
        style: 'light',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Getting started', to: '/getting-started' },
              { label: 'Diagnostics', to: '/rules' },
            ],
          },
          {
            title: 'Project',
            items: [
              { label: 'GitHub', href: repository },
              { label: 'NuGet', href: 'https://www.nuget.org/packages/Pledge' },
              { label: 'Sponsor', href: 'https://github.com/sponsors/pagebrooks' },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} Page Brooks. MIT licensed.`,
      },
      prism: {
        theme: codeTheme,
        darkTheme: codeTheme,
        additionalLanguages: ['csharp', 'bash', 'powershell'],
      },
    }),
};

export default config;
