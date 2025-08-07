// https://vitepress.dev/guide/custom-theme
import { h, onMounted, watch, nextTick } from 'vue'
import { useRoute } from 'vitepress'
import mermaid from 'mermaid'
import DefaultTheme from 'vitepress/theme'

import './custom.css'
import './style.css'

export default {
  extends: DefaultTheme,
  Layout: () => {
    return h(DefaultTheme.Layout, null, {
      // https://vitepress.dev/guide/extending-default-theme#layout-slots
    })
  },
  enhanceApp({ app, router, siteData }) {
    // ...
  },
  setup() {
    const route = useRoute()
    
    const initMermaid = async () => {
      mermaid.initialize({
        startOnLoad: false,
        theme: 'default',
        themeVariables: {
          primaryColor: '#4f46e5',
          primaryTextColor: '#1f2937',
          primaryBorderColor: '#e5e7eb',
          lineColor: '#6b7280',
          secondaryColor: '#f3f4f6',
          tertiaryColor: '#ffffff'
        },
        flowchart: {
          useMaxWidth: true,
          htmlLabels: true,
          curve: 'basis'
        },
        sequence: {
          diagramMarginX: 50,
          diagramMarginY: 10,
          actorMargin: 50,
          width: 150,
          height: 65,
          boxMargin: 10,
          boxTextMargin: 5,
          noteMargin: 10,
          messageMargin: 35,
          mirrorActors: true,
          bottomMarginAdj: 1,
          useMaxWidth: true,
          rightAngles: false,
          showSequenceNumbers: false
        }
      })
      
      await nextTick()
      
      const mermaidElements = document.querySelectorAll('.language-mermaid')
      if (mermaidElements.length > 0) {
        for (let i = 0; i < mermaidElements.length; i++) {
          const element = mermaidElements[i]
          const code = element.textContent
          
          try {
            const { svg } = await mermaid.render(`mermaid-${i}`, code)
            element.innerHTML = svg
            element.classList.remove('language-mermaid')
            element.classList.add('mermaid-rendered')
          } catch (error) {
            console.error('Mermaid rendering error:', error)
            element.innerHTML = `<pre class="mermaid-error">Mermaid Error: ${error.message}\n\nCode:\n${code}</pre>`
          }
        }
      }
    }

    onMounted(() => {
      initMermaid()
    })

    watch(
      () => route.path,
      () => nextTick(() => initMermaid()),
      { immediate: true }
    )
  }
}
