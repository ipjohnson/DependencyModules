import { defineConfig } from 'vitepress';

// Published to https://ipjohnson.github.io/DependencyModules/, so every absolute path needs the
// repository name as a base. Getting this wrong is the classic Pages failure: the site builds, the
// landing page loads, and every asset and internal link 404s.
const base = '/DependencyModules/';

export default defineConfig({
  title: 'DependencyModules',
  description:
    'Compile-time dependency injection for .NET. Attributes and conventions become registration ' +
    'code at build time — no reflection, no assembly scanning, trimming and Native AOT safe.',
  base,
  lang: 'en-GB',
  cleanUrls: true,

  // A broken internal link should fail the build rather than ship. The docs cross-reference heavily
  // and a rename would otherwise rot links silently.
  ignoreDeadLinks: false,

  head: [
    ['link', { rel: 'icon', href: `${base}favicon.svg`, type: 'image/svg+xml' }],
    ['meta', { name: 'theme-color', content: '#6d5bd5' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'DependencyModules' }],
    [
      'meta',
      {
        property: 'og:description',
        content: 'Compile-time dependency injection for .NET. No reflection, AOT safe.',
      },
    ],
  ],

  themeConfig: {
    siteTitle: 'DependencyModules',

    nav: [
      { text: 'Guide', link: '/guide/getting-started', activeMatch: '/guide/' },
      { text: 'Reference', link: '/reference/diagnostics', activeMatch: '/reference/' },
      {
        text: 'NuGet',
        items: [
          { text: 'Runtime', link: 'https://www.nuget.org/packages/DependencyModules.Runtime/' },
          {
            text: 'SourceGenerator',
            link: 'https://www.nuget.org/packages/DependencyModules.SourceGenerator/',
          },
          {
            text: 'Conventions',
            link: 'https://www.nuget.org/packages/DependencyModules.Conventions/',
          },
          { text: 'xUnit', link: 'https://www.nuget.org/packages/DependencyModules.xUnit/' },
        ],
      },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Getting started',
          items: [
            { text: 'Installation', link: '/guide/getting-started' },
            { text: 'Modules', link: '/guide/modules' },
            { text: 'Registering services', link: '/guide/services' },
          ],
        },
        {
          text: 'Testing',
          items: [
            { text: 'Testing modules', link: '/guide/testing' },
            { text: 'Mocks and values', link: '/guide/testing-mocks' },
            { text: 'Testing registrations', link: '/guide/testing-registrations' },
          ],
        },
        {
          text: 'Registering in bulk',
          items: [
            { text: 'Conventions', link: '/guide/conventions' },
            { text: 'Scanning a package', link: '/guide/scanning' },
          ],
        },
        {
          text: 'Changing behaviour',
          items: [
            { text: 'Decorators', link: '/guide/decorators' },
            { text: 'Interception', link: '/guide/interception' },
            { text: 'Environments', link: '/guide/environments' },
          ],
        },
        {
          text: 'Everything else',
          items: [
            { text: 'Trimming and AOT', link: '/guide/aot' },
            { text: 'Troubleshooting', link: '/guide/troubleshooting' },
            { text: 'Writing your own generator', link: '/guide/extending' },
          ],
        },
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Diagnostics', link: '/reference/diagnostics' },
            { text: 'Attributes', link: '/reference/attributes' },
            { text: 'Convention API', link: '/reference/conventions-api' },
            { text: 'MSBuild properties', link: '/reference/msbuild' },
          ],
        },
      ],
    },

    socialLinks: [{ icon: 'github', link: 'https://github.com/ipjohnson/DependencyModules' }],

    search: { provider: 'local' },

    editLink: {
      pattern: 'https://github.com/ipjohnson/DependencyModules/edit/main/website/:path',
      text: 'Edit this page on GitHub',
    },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © Ian Johnson',
    },

    outline: [2, 3],
  },
});
