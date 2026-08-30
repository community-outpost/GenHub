import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

export default withMermaid(
    defineConfig({
        title: 'GeneralsHub',
        description: 'C&C Launcher Documentation',
        base:
            process.env.NODE_ENV === 'production' ||
                process.env.GITHUB_ACTIONS === 'true'
                ? '/wiki/'
                : '/',

        ignoreDeadLinks: true,

        head: [
            ['link', { rel: 'icon', href: '/assets/icon.png' }]
        ],

        themeConfig: {
            logo: './assets/logo.png',

            nav: [
                { text: 'Home', link: '/' },
                { text: 'Get Started', link: '/onboarding' },
                { text: 'Architecture', link: '/architecture' },
                { text: 'Features', link: '/features/index' },
                { text: 'API Reference', link: '/dev/index' },
                { text: 'Flowcharts', link: '/FlowCharts/' }
            ],

            sidebar: [
                {
                    text: 'Getting Started',
                    items: [
                        { text: 'Introduction', link: '/' },
                        { text: 'Developer Onboarding', link: '/onboarding' },
                        { text: 'Architecture Overview', link: '/architecture' },
                        { text: 'Velopack Integration', link: '/velopack-integration' }
                    ]
                },
                {
                    text: 'Features',
                    items: [
                        { text: 'Overview', link: '/features/index' },
                        { text: 'App Update & Installer', link: '/velopack-integration' },
                        { text: 'Content System', link: '/features/content' },
                        { text: 'Content Reconciliation', link: '/features/reconciliation' },
                        { text: 'Manifest Service', link: '/features/manifest' },
                        { text: 'Storage & CAS', link: '/features/storage' },
                        { text: 'Validation', link: '/features/validation' },
                        { text: 'Workspace', link: '/features/workspace' },
                        { text: 'Launching', link: '/features/launching' },
                        { text: 'GameProfiles System', link: '/features/gameprofiles' },
                        { text: 'Game Installations', link: '/features/game-installations/' },
                        { text: 'User Data Management', link: '/features/userdata' },
                        { text: 'Downloads UI', link: '/features/downloads-ui' },
                        { text: 'Notifications', link: '/features/notifications' },
                        { text: 'Desktop Shortcuts', link: '/features/desktop-shortcuts' },
                        { text: 'Steam Proxy Launcher', link: '/features/steam-proxy-launcher' },
                        { text: 'Danger Zone', link: '/features/danger-zone' }
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
                    text: 'API Reference',
                    items: [
                        { text: 'Overview', link: '/dev/index' },
                        { text: 'Result Pattern', link: '/dev/result-pattern' },
                        { text: 'Constants', link: '/dev/constants' },
                        { text: 'Models', link: '/dev/models' },
                        { text: 'Manifest ID System', link: '/dev/manifest-id-system' },
                        { text: 'Content Manifest', link: '/dev/content-manifest' },
                        { text: 'Game Settings Architecture', link: '/dev/game-settings-architecture' },
                        { text: 'Uploading API', link: '/dev/uploading-api' },
                        { text: 'Debugging', link: '/dev/debugging' }
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
                        { text: 'Complete User Flow', link: '/FlowCharts/Complete-User-Flow' },
                        { text: 'CAS Storage Flow', link: '/FlowCharts/CAS-Storage-Flow' },
                        { text: 'Dependency Resolution', link: '/FlowCharts/Dependency-Resolution-Flow' },
                        { text: 'Profile Lifecycle', link: '/FlowCharts/Profile-Lifecycle-Flow' },
                        { text: 'Publisher Studio Workflow', link: '/FlowCharts/Publisher-Studio-Workflow' },
                        { text: 'Subscription System', link: '/FlowCharts/Subscription-System-Flow' }
                    ]
                },
                {
                    text: 'Tools',
                    items: [
                        { text: 'Overview', link: '/tools/' },
                        { text: 'Replay Manager', link: '/tools/replay-manager' },
                        { text: 'Map Manager', link: '/tools/map-manager' }
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
            class: 'mermaid my-class'
        }
    }),

    // Mermaid configuration
    {
        theme: 'default'
    }
)
