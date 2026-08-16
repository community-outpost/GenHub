# 1. Base branch

git checkout main
git pull

# 2. Create feature branch

git checkout -b refactor/centralized-download-service

# 3. Commit 1: Define IDownloadService interface

git add GenHub.Core/Interfaces/Common/IDownloadService.cs
git commit -m "refactor(core): Add IDownloadService interface for common download operations"

# 4. Commit 2: Implement DownloadService

git add GenHub/Services/DownloadService.cs
git commit -m "refactor(download): Implement DownloadService for centralized file downloads"

# 5. Commit 3: Integrate DownloadService into FileOperationsService

git add GenHub/Features/Workspace/FileOperationsService.cs
git commit -m "refactor(workspace): Use IDownloadService in FileOperationsService"

# 6. Commit 4: Integrate DownloadService into AppUpdateService

git add GenHub/Features/AppUpdate/Services/AppUpdateService.cs
git commit -m "refactor(appupdate): Use IDownloadService in AppUpdateService"

# 7. Commit 5: Add DownloadModule and update AppServices

git add GenHub/Infrastructure/DependencyInjection/DownloadModule.cs \
        GenHub/Infrastructure/DependencyInjection/AppServices.cs
git commit -m "refactor(di): Add DownloadModule and register centralized download service"

# 8. Push branch

git push --set-upstream origin refactor/centralized-download-service
