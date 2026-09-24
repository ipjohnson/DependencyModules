import { defineConfig } from 'vitepress'

const description = 'A source generator for Microsoft.Extensions.DependencyInjection registrations.'

const packages = [
  'Runtime',
  'SourceGenerator',
  'Testing',
  'xUnit',
  'NUnit',
  'NSubstitute',
  'Moq',
  'FakeItEasy',
  'SourceGenerator.Impl',
]

export default defineConfig({
  lang: 'en-US',
  title: 'DependencyModules',
  description,
  base: '/DependencyModules/',
  cleanUrls: true,
  lastUpdated: true,
  ignoreDeadLinks: false,
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/DependencyModules/favicon.svg' }],
    ['meta', { name: 'theme-color', content: '#6d5bd5' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'DependencyModules' }],
    ['meta', { property: 'og:description', content: description }],
  ],
  themeConfig: {
    logo: { light: '/logo.svg', dark: '/logo-dark.svg' },
    outline: [2, 3],
    nav: [
      { text: 'Guide', link: '/guide/getting-started', activeMatch: '/guide/' },
      { text: 'Reference', link: '/reference/attributes', activeMatch: '/reference/' },
      {
        text: 'NuGet',
        items: packages.map((name) => ({
          text: `DependencyModules.${name}`,
          link: `https://www.nuget.org/packages/DependencyModules.${name}/`,
        })),
      },
    ],
    sidebar: [
      {
        text: 'Introduction',
        items: [{ text: 'Getting started', link: '/guide/getting-started' }],
      },
      {
        text: 'Registration',
        items: [
          { text: 'Services', link: '/guide/services' },
          { text: 'Modules', link: '/guide/modules' },
          { text: 'Conventions', link: '/guide/conventions' },
          { text: 'Decorators', link: '/guide/decorators' },
          { text: 'Interception', link: '/guide/interception' },
          { text: 'Environments', link: '/guide/environments' },
        ],
      },
      {
        text: 'Testing',
        items: [
          { text: 'Write tests', link: '/guide/testing' },
          { text: 'xUnit', link: '/guide/testing-xunit' },
          { text: 'NUnit', link: '/guide/testing-nunit' },
          { text: 'Mocks', link: '/guide/testing-mocking' },
          { text: 'More service providers', link: '/guide/testing-container-source' },
        ],
      },
      {
        text: 'Advanced',
        items: [
          { text: 'Native AOT and trimming', link: '/guide/aot' },
          { text: 'Extending', link: '/guide/extending' },
          { text: 'Troubleshooting', link: '/guide/troubleshooting' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Attributes', link: '/reference/attributes' },
          { text: 'MSBuild properties', link: '/reference/msbuild' },
          { text: 'Diagnostics', link: '/reference/diagnostics' },
          { text: 'API', link: '/reference/api' },
        ],
      },
    ],
    socialLinks: [{ icon: 'github', link: 'https://github.com/ipjohnson/DependencyModules' }],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/ipjohnson/DependencyModules/edit/main/website/:path',
      text: 'Change this page on GitHub',
    },
    footer: {
      message: 'MIT license',
      copyright: 'Copyright (c) Ian Johnson',
    },
  },
})
