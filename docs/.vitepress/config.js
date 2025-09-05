import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

export default withMermaid(
    defineConfig({
    title: 'GeneralsHub',
    description: 'C&C Launcher Documentation',
    // Use a root base during local development so vitepress dev serves at '/'
    // and only use the '/wiki/' base for production (GitHub Pages).
    base: (process.env.NODE_ENV === 'production' || process.env.GITHUB_ACTIONS === 'true') ? '/wiki/' : '/',

    head: [
        ['link', { rel: 'icon', href: '/assets/icon.png' }]
    ],

    themeConfig: {
        logo: './assets/logo.png',

        nav: [
            { text: 'Home', link: '/' },
            { text: 'Get Started', link: '/onboarding' },
            { text: 'Architecture', link: '/architecture' },
            { text: 'Flowcharts', link: '/FlowCharts/' }
        ],

        sidebar: [
            {
                text: 'Getting Started',
                items: [
                    { text: 'Introduction', link: '/' },
                    { text: 'Developer Onboarding', link: '/onboarding' },
                    { text: 'Architecture Overview', link: '/architecture' }
                ]
            },
            {
                text: 'Converters',
                items: [
                    { text: 'Overview', link: '/dev/converters/' },
                    { text: 'Boolean Converters', link: '/dev/converters/bool-converters' },
                    { text: 'Null Converters', link: '/dev/converters/null-converters' },
                    { text: 'String Converters', link: '/dev/converters/string-converters' },
                    { text: 'Color Converters', link: '/dev/converters/color-converters' },
                    { text: 'Profile Converters', link: '/dev/converters/profile-converters' },
                    { text: 'Enum Converters', link: '/dev/converters/enum-converters' },
                    { text: 'Navigation Converters', link: '/dev/converters/navigation-converters' },
                    { text: 'Data Type Converters', link: '/dev/converters/data-type-converters' }
                ]
            },
            {
                text: 'System Flowcharts',
                items: [
                    { text: 'Overview', link: '/FlowCharts/' },
                    { text: 'Game Detection', link: '/FlowCharts/Detection-Flow' },
                    { text: 'Content Discovery', link: '/FlowCharts/Discovery-Flow' },
                    { text: 'Content Resolution', link: '/FlowCharts/Resolution-Flow' },
                    { text: 'Content Acquisition', link: '/FlowCharts/Acquisition-Flow' },
                    { text: 'Workspace Assembly', link: '/FlowCharts/Assembly-Flow' },
                    { text: 'Manifest Creation', link: '/FlowCharts/Manifest-Creation-Flow' },
                    { text: 'Complete User Flow', link: '/FlowCharts/Complete-User-Flow' }
                ]
            }
        ],

        socialLinks: [
            { icon: 'github', link: 'https://github.com/community-outpost/GenHub' }
        ],

        footer: {
            message: 'GeneralsHub Docs',
            copyright: '© 2025 GeneralsHub'
        }
    },

    // Mermaid configuration
    mermaid: {
        theme: 'default'
    }
})
)
