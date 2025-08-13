import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

export default withMermaid(
  defineConfig({
    title: 'GeneralsHub',
    description: 'Universal C&C Launcher Documentation',
    base: '/GeneralsHubWiki/',
    
    themeConfig: {
      nav: [
        { text: 'Home', link: '/' },
        { text: 'Architecture', link: '/architecture' },
        { text: 'Flowcharts', link: '/FlowCharts/' }
      ],
      
      sidebar: [
        {
          text: 'Overview',
          items: [
            { text: 'Introduction', link: '/' },
            { text: 'Architecture', link: '/architecture' }
          ]
        },
        {
          text: 'Flowcharts',
          items: [
            { text: 'Content Discovery', link: '/FlowCharts/Discovery-Flow' },
            { text: 'Content Resolution', link: '/FlowCharts/Resolution-Flow' },
            { text: 'Content Acquisition', link: '/FlowCharts/Acquisition-Flow' },
            { text: 'Workspace Assembly', link: '/FlowCharts/Assembly-Flow' },
            { text: 'Complete User Flow', link: '/FlowCharts/Complete-User-Flow' }
          ]
        }
      ],
      
      socialLinks: [
        { icon: 'github', link: 'https://github.com/community-outpost/GeneralsHub' }
      ]
    },
    
    // Mermaid configuration
    mermaid: {
      theme: 'default',
      themeVariables: {
        primaryColor: '#ff6b6b',
        primaryTextColor: '#fff',
        primaryBorderColor: '#ff4757',
        lineColor: '#5f5f5f',
        secondaryColor: '#2ed573',
        tertiaryColor: '#1e90ff'
      }
    },
    
    mermaidPlugin: {
      class: 'mermaid my-class', // set additional css classes for parent container 
    }
  })
)
