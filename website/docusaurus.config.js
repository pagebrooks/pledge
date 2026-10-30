// @ts-check
import { themes as prismThemes } from 'prism-react-renderer';

const repository = 'https://github.com/pagebrooks/pledge';

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'Pledge',
  tagline: 'Design by Contract for .NET',
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
      navbar: {
        title: 'Pledge',
        items: [
          { type: 'docSidebar', sidebarId: 'docs', label: 'Docs', position: 'left' },
          { type: 'docSidebar', sidebarId: 'rules', docsPluginId: 'rules', label: 'Diagnostics', position: 'left' },
          { href: 'https://www.nuget.org/packages/Pledge', label: 'NuGet', position: 'right' },
          { href: 'https://github.com/sponsors/pagebrooks', label: 'Sponsor', position: 'right' },
          { href: repository, label: 'GitHub', position: 'right' },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Docs',
            items: [
              { label: 'Getting started', to: '/' },
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
        theme: prismThemes.github,
        darkTheme: prismThemes.dracula,
        additionalLanguages: ['csharp', 'bash', 'powershell'],
      },
    }),
};

export default config;
