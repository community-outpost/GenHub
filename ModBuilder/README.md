# ModBuilder Documentation

**Status**: Under Development (Porting to C# / Avalonia)  
**Target Platform**: GenHub Tools Integration  
**Last Updated**: August 2026  

---

## Directory Navigation & Structure

```
ModBuilder/
├── README.md                          (Root index)
│
├── 01_Requirements/                   (4 files)
│   ├── TRANSCRIPT_REQUIREMENTS.md     Python requirements from transcript
│   ├── TRANSCRIPT_ANALYSIS_SUMMARY.md Python codebase analysis
│   ├── IMPLEMENTATION_PLAN.md         C# porting strategy
│   └── MBPROJ_FORMAT.md               Project file format spec
│
├── 02_Technical_Specs/                (6 files)
│   ├── MASTER_CSHARP_PORTING_SPECIFICATION.md Complete porting specification
│   ├── CSHARP_PORTING_GUIDE_UI_AND_FLOW.md    UI and MVVM flow architecture
│   ├── PRODUCTION_PATTERNS_ANALYSIS.md        Real-world modding patterns analysis
│   ├── PRODUCTION_PROJECT_COMPLETE_ANALYSIS.md Analysis of production mod projects
│   ├── GAME_MODIFICATIONS_GUIDE.md            Generals / Zero Hour modding integration
│   └── SETTINGS.md                            Tool, build, and runner settings
│
├── 03_Implementation/                 (10 files)
│   ├── BENCHMARK_STRATEGY.md          Performance and memory benchmarking plan
│   ├── BUILD_ENGINE_IMPLEMENTATION.md 5-stage build engine pipeline specification
│   ├── COMPLETE_IMPLEMENTATION_REPORT.md Implementation report
│   ├── COMPLETION_REPORT.md           Service completion milestones
│   ├── CRITICAL_PERFORMANCE_ISSUES.md Bottleneck analysis and caching solutions
│   ├── CURRENT_STATE_AND_NEXT_STEPS.md Status and roadmap
│   ├── FINAL_IMPLEMENTATION_SUMMARY.md Summary of implemented services
│   ├── FINAL_VERIFICATION_STATUS.md   Service verification checklist
│   ├── IMPLEMENTATION_COMPLETE.md     Module registration and composition root
│   └── VERIFICATION_REPORT.md         Test and verification results
│
├── 04_User_Documentation/             (7 files)
│   ├── USER_GUIDE.md                  End-user guide for ModBuilder
│   ├── DEPLOYMENT_GUIDE.md            Deployment and release packaging instructions
│   ├── MANUAL_TESTING_GUIDE.md        Manual QA testing instructions
│   ├── MANUAL_TEST_PLAN.md            Manual QA test cases
│   ├── MODBUILDER_COMPLETE_GUIDE.md   Comprehensive user and authoring guide
│   ├── PRODUCTION_READY_CHECKLIST.md  Pre-release checklist
│   └── TESTING_GUIDE.md               Automated unit and integration test guide
│
└── 05_Archive/                        (20 files)
    ├── CODE_ANALYSIS_REPORT.md
    ├── COMPLETE_TESTING_REPORT.md
    ├── DEBUG_TRACE.md
    ├── DOCUMENTATION_AUDIT_REPORT.md
    ├── DOCUMENTATION_CONSOLIDATION_COMPLETE.md
    ├── DOCUMENTATION_CONSOLIDATION_PLAN.md
    ├── EXECUTIVE_SUMMARY.md
    ├── FINAL_REPORT.md
    ├── FIXES_APPLIED.md
    ├── PERFORMANCE_REVIEW_SUMMARY.md
    ├── PYTHON_MODBUILDER_ANALYSIS.md
    ├── PYTHON_PROJECT_ANALYSIS.md
    ├── REAL_WORKFLOW_INVESTIGATION.md
    ├── ROOT_CAUSE_ANALYSIS.md
    ├── TESTING_SUMMARY.md
    ├── TEST_SIMPLIFIED_CONFIG_CONVERSION.md
    ├── WEEK_1_COMPLETION_REPORT.md
    ├── WEEK_2_COMPLETION_SUMMARY.md
    ├── WEEK_3_COMPLETION_SUMMARY.md
    └── WORKFLOW_FIX_COMPLETE.md
```

---

## Guide for Code Reviewers

1. **Architecture & Requirements**:
   - Start with [`01_Requirements/MBPROJ_FORMAT.md`](file:///home/ubuntu/workspaces/GenHub/ModBuilder/01_Requirements/MBPROJ_FORMAT.md) and [`02_Technical_Specs/MASTER_CSHARP_PORTING_SPECIFICATION.md`](file:///home/ubuntu/workspaces/GenHub/ModBuilder/02_Technical_Specs/MASTER_CSHARP_PORTING_SPECIFICATION.md).
2. **Build Pipeline & Caching**:
   - Review [`03_Implementation/BUILD_ENGINE_IMPLEMENTATION.md`](file:///home/ubuntu/workspaces/GenHub/ModBuilder/03_Implementation/BUILD_ENGINE_IMPLEMENTATION.md) and [`03_Implementation/CRITICAL_PERFORMANCE_ISSUES.md`](file:///home/ubuntu/workspaces/GenHub/ModBuilder/03_Implementation/CRITICAL_PERFORMANCE_ISSUES.md).
3. **UI, Workflow & Root Causes**:
   - Review [`04_User_Documentation/MODBUILDER_COMPLETE_GUIDE.md`](file:///home/ubuntu/workspaces/GenHub/ModBuilder/04_User_Documentation/MODBUILDER_COMPLETE_GUIDE.md) and [`05_Archive/ROOT_CAUSE_ANALYSIS.md`](file:///home/ubuntu/workspaces/GenHub/ModBuilder/05_Archive/ROOT_CAUSE_ANALYSIS.md).
