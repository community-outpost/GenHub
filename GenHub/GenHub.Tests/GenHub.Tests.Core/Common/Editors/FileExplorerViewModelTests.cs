using GenHub.Common.Editors;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Editors;

/// <summary>
/// Unit tests for <see cref="FileExplorerViewModel"/>.
/// </summary>
public sealed class FileExplorerViewModelTests
{
    /// <summary>
    /// Verifies that setting a directory builds the tree with matching files only.
    /// </summary>
    [Fact]
    public void Directory_SetToFolder_ListsMatchingFiles()
    {
        string root = CreateTree();
        try
        {
            var explorer = new FileExplorerViewModel
            {
                FilePatterns = ["*.wnd"],
                ShowFileExtensions = false,
            };

            explorer.Directory = root;

            Assert.Single(explorer.Nodes);
            var files = explorer.Nodes[0].Children.Where(node => node.IsFile).ToList();
            Assert.Single(files);
            Assert.Equal("MainMenu", files[0].Name);
            Assert.Equal("MainMenu.wnd", files[0].FileName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that overlapping file patterns list each file only once.
    /// </summary>
    [Fact]
    public void Directory_OverlappingPatterns_ListsEachFileOnce()
    {
        string root = CreateTree();
        try
        {
            var explorer = new FileExplorerViewModel
            {
                FilePatterns = ["*.wnd", "Main*"],
            };

            explorer.Directory = root;

            Assert.Single(explorer.Nodes);
            var files = explorer.Nodes[0].Children.Where(node => node.IsFile).ToList();
            Assert.Single(files);
            Assert.Equal("MainMenu.wnd", files[0].FileName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that excluded and hidden directories are skipped.
    /// </summary>
    [Fact]
    public void Directory_SetToFolder_SkipsExcludedAndHiddenDirectories()
    {
        string root = CreateTree();
        try
        {
            var explorer = new FileExplorerViewModel
            {
                FilePatterns = ["*.wnd"],
                ExcludedDirectoryNames = ["Build"],
            };

            explorer.Directory = root;

            var directories = explorer.Nodes[0].Children.Where(node => node.IsDirectory).Select(node => node.Name).ToList();
            Assert.DoesNotContain("Build", directories);
            Assert.DoesNotContain(".hidden", directories);
            Assert.Contains("Nested", directories);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that the current path is marked on the matching file node.
    /// </summary>
    [Fact]
    public void CurrentPath_SetToFile_MarksOnlyThatNode()
    {
        string root = CreateTree();
        try
        {
            var explorer = new FileExplorerViewModel { FilePatterns = ["*.wnd"] };
            explorer.Directory = root;

            string nested = Path.Combine(root, "Nested", "Child.wnd");
            explorer.CurrentPath = nested;

            var current = explorer.Nodes[0].Children.SelectMany(Flatten).Where(node => node.IsCurrent).ToList();
            Assert.Single(current);
            Assert.Equal(nested, current[0].FullPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that FindFirstFile returns the first file depth first.
    /// </summary>
    [Fact]
    public void FindFirstFile_PopulatedTree_ReturnsFirstFile()
    {
        string root = CreateTree();
        try
        {
            var explorer = new FileExplorerViewModel
            {
                FilePatterns = ["*.wnd"],
                ExcludedDirectoryNames = ["Build"],
            };
            explorer.Directory = root;

            Assert.Equal(Path.Combine(root, "Nested", "Child.wnd"), explorer.FindFirstFile());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that activating a file raises FileActivated and directories are ignored.
    /// </summary>
    [Fact]
    public void OpenFile_FileNode_RaisesFileActivated()
    {
        string root = CreateTree();
        try
        {
            var explorer = new FileExplorerViewModel { FilePatterns = ["*.wnd"] };
            explorer.Directory = root;

            EditorFileTreeNodeViewModel? activated = null;
            explorer.FileActivated += (_, node) => activated = node;

            var file = explorer.Nodes[0].Children.First(node => node.IsFile);
            explorer.OpenFileCommand.Execute(file);

            Assert.Same(file, activated);

            activated = null;
            var directory = explorer.Nodes[0].Children.First(node => node.IsDirectory);
            explorer.OpenFileCommand.Execute(directory);

            Assert.Null(activated);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that browse sets the picked directory and invokes the adopt callback.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Browse_PickedFolder_SetsDirectoryAndAdoptsAsync()
    {
        string root = CreateTree();
        string? adopted = null;
        try
        {
            var explorer = new FileExplorerViewModel { FilePatterns = ["*.wnd"] };
            explorer.BrowseFolderAsync = _ => Task.FromResult<string?>(root);
            explorer.DirectoryAdoptedAsync = (folder, _) =>
            {
                adopted = folder;
                return Task.CompletedTask;
            };

            Assert.True(explorer.CanBrowse);
            await explorer.BrowseCommand.ExecuteAsync(null);

            Assert.Equal(root, explorer.Directory);
            Assert.Equal(root, adopted);
            Assert.Single(explorer.Nodes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTree()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "MainMenu.wnd"), "root");
        File.WriteAllText(Path.Combine(root, "Notes.txt"), "ignored");
        Directory.CreateDirectory(Path.Combine(root, "Nested"));
        File.WriteAllText(Path.Combine(root, "Nested", "Child.wnd"), "child");
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        File.WriteAllText(Path.Combine(root, "Build", "Generated.wnd"), "excluded");
        Directory.CreateDirectory(Path.Combine(root, ".hidden"));
        File.WriteAllText(Path.Combine(root, ".hidden", "Secret.wnd"), "hidden");
        return root;
    }

    private static System.Collections.Generic.IEnumerable<EditorFileTreeNodeViewModel> Flatten(EditorFileTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }
}
