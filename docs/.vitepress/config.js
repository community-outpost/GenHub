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
            theme: 'default',
            themeVariables: {
                primaryColor: '#7c3aed',
                primaryTextColor: '#fff',
                primaryBorderColor: '#6b46c1',
                lineColor: '#5f5f5f',
                secondaryColor: '#2ed573',
                tertiaryColor: '#1e90ff'
            }
        },
        
        // Optional: Configure mermaid for dark mode
        mermaidPlugin: {
            class: 'mermaid my-class', // set additional css classes for parent container 
        }
    })
)
