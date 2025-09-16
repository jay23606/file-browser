using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Text.Json;

namespace TestProject.Controllers
{
    /// <summary>
    /// Provides file and folder management operations on the server, including browsing, searching, uploading,
    /// renaming, moving, copying, deleting, creating folders, and downloading as ZIP.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {

        readonly ILogger<TestController> _logger;
        readonly string _rootDirectory;

        public TestController(ILogger<TestController> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _rootDirectory = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        /// <summary>
        /// Browses the specified directory and returns folders and files.
        /// </summary>
        /// <param name="path">Relative path from root directory.</param>
        [HttpGet("browse")]
        public IActionResult BrowseDirectory(string path = "")
        {
            string fullPath = Path.Combine(_rootDirectory, path);
            if (!Directory.Exists(fullPath)) return NotFound("Directory not found");

            var folderInfo = new DirectoryInfo(fullPath);
            var folders = folderInfo.GetDirectories().Select(d => new
            {
                Name = d.Name,
                DateModified = d.LastWriteTime.ToString("M/d/yyyy h:mm tt"),
                FileCount = d.GetFiles().Length
            });
            var files = folderInfo.GetFiles().Select(f => new
            {
                Name = f.Name,
                DateModified = f.LastWriteTime.ToString("M/d/yyyy h:mm tt"),
                Size = f.Length
            });

            var result = new
            {
                Folders = folders,
                Files = files
            };

            return Ok(result);
        }

        /// <summary>
        /// Searches for files matching the query, supports wildcards.
        /// </summary>
        /// <param name="query">Search query or pattern.</param>
        [HttpGet("search")]
        public IActionResult SearchFiles(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Ok("[]");
            if (!query.Contains("*")) query = $"*{query}*";
            var files = Directory.GetFiles(_rootDirectory, query, SearchOption.AllDirectories)
                .Select(f => new
                {
                    Name = Path.GetFileName(f),
                    Path = f,
                    Size = new FileInfo(f).Length
                });
            return Ok(JsonSerializer.Serialize(files));
        }

        /// <summary>
        /// Uploads files to the specified path.
        /// </summary>
        /// <param name="files">Files to upload.</param>
        /// <param name="path">Relative target path.</param>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadFiles(List<IFormFile> files, [FromForm] string path = "")
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files selected" });

            var targetDir = Path.Combine(_rootDirectory, path);
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            foreach (var file in files)
            {
                var filePath = Path.Combine(targetDir, file.FileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }

            return Ok(new { message = $"{files.Count} file(s) uploaded successfully" });
        }

        public class DeleteRequest
        {
            public string Path { get; set; }
            public List<ItemToDelete> Items { get; set; }
        }

        public class ItemToDelete
        {
            public string Name { get; set; }
            public string Type { get; set; } // "file" or "folder"
        }

        /// <summary>
        /// Deletes specified files or folders.
        /// </summary>
        [HttpPost("delete")]
        public IActionResult Delete([FromBody] DeleteRequest request)
        {
            var targetDir = Path.Combine(_rootDirectory, request.Path ?? "");

            foreach (var item in request.Items)
            {
                try
                {
                    var fullPath = Path.Combine(targetDir, item.Name);
                    if (item.Type == "file" && System.IO.File.Exists(fullPath))
                        System.IO.File.Delete(fullPath);
                    else if (item.Type == "folder" && Directory.Exists(fullPath))
                        Directory.Delete(fullPath, true);
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = ex.Message });
                }
            }

            return Ok(new { message = $"{request.Items.Count} item(s) deleted" });
        }

        public class NewFolderRequest
        {
            public string Path { get; set; } = "";
            public string FolderName { get; set; } = "";
        }

        /// <summary>
        /// Creates a new folder at the specified path.
        /// </summary>
        [HttpPost("newfolder")]
        public IActionResult CreateFolder([FromBody] NewFolderRequest request)
        {
            try
            {
                var targetDir = Path.Combine(_rootDirectory, request.Path ?? "");
                var newFolderPath = Path.Combine(targetDir, request.FolderName);

                if (Directory.Exists(newFolderPath))
                    return BadRequest(new { message = "Folder already exists" });

                Directory.CreateDirectory(newFolderPath);

                return Ok(new { message = $"Folder '{request.FolderName}' created" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        public class MoveCopyRequest
        {
            public string SourcePath { get; set; }     // current folder path from pathBox
            public string DestinationPath { get; set; } // target folder path
            public List<ItemDto> Items { get; set; }    // files/folders selected
        }

        public class ItemDto
        {
            public string Name { get; set; }
            public string Type { get; set; } // "file" or "folder"
        }

        /// <summary>
        /// Moves selected files or folders from source to destination.
        /// </summary>
        [HttpPost("move")]
        public IActionResult Move([FromBody] MoveCopyRequest request)
        {
            try
            {

                string sourceDir = string.IsNullOrWhiteSpace(request.SourcePath) || request.SourcePath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.SourcePath.Replace('\\', Path.DirectorySeparatorChar)
                                          .Replace('/', Path.DirectorySeparatorChar)
                                          .TrimStart('.', Path.DirectorySeparatorChar)));

                string destDir = string.IsNullOrWhiteSpace(request.DestinationPath) || request.DestinationPath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.DestinationPath.Replace('\\', Path.DirectorySeparatorChar)
                                               .Replace('/', Path.DirectorySeparatorChar)
                                               .TrimStart('.', Path.DirectorySeparatorChar)));

                // prevent escaping outside root
                if (!sourceDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !destDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Invalid path traversal attempt" });

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                foreach (var item in request.Items)
                {
                    var sourcePath = Path.Combine(sourceDir, item.Name);
                    var destPath = Path.Combine(destDir, item.Name);

                    if (item.Type == "file" && System.IO.File.Exists(sourcePath))
                        System.IO.File.Move(sourcePath, destPath, overwrite: true);
                    else if (item.Type == "folder" && Directory.Exists(sourcePath))
                    {
                        if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                        Directory.Move(sourcePath, destPath);
                    }
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Copies selected files or folders from source to destination.
        /// </summary>
        [HttpPost("copy")]
        public IActionResult Copy([FromBody] MoveCopyRequest request)
        {
            try
            {
                string sourceDir = string.IsNullOrWhiteSpace(request.SourcePath) || request.SourcePath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.SourcePath.Replace('\\', Path.DirectorySeparatorChar)
                                          .Replace('/', Path.DirectorySeparatorChar)
                                          .TrimStart('.', Path.DirectorySeparatorChar)));

                string destDir = string.IsNullOrWhiteSpace(request.DestinationPath) || request.DestinationPath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.DestinationPath.Replace('\\', Path.DirectorySeparatorChar)
                                               .Replace('/', Path.DirectorySeparatorChar)
                                               .TrimStart('.', Path.DirectorySeparatorChar)));

                if (!sourceDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !destDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Invalid path traversal attempt" });

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                foreach (var item in request.Items)
                {
                    var sourcePath = Path.Combine(sourceDir, item.Name);
                    var destPath = Path.Combine(destDir, item.Name);

                    if (item.Type == "file" && System.IO.File.Exists(sourcePath))
                        System.IO.File.Copy(sourcePath, destPath, overwrite: true);
                    else if (item.Type == "folder" && Directory.Exists(sourcePath))
                        CopyDirectory(sourcePath, destPath);
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, overwrite: true);
            }

            foreach (var folder in Directory.GetDirectories(sourceDir))
            {
                var destFolder = Path.Combine(destDir, Path.GetFileName(folder));
                CopyDirectory(folder, destFolder);
            }
        }

        public class RenameRequest
        {
            public string Path { get; set; } = "";
            public string OldName { get; set; }
            public string NewName { get; set; }
            public string Type { get; set; } // "file" or "folder"
        }

        /// <summary>
        /// Renames a file or folder at the specified path.
        /// </summary>
        [HttpPost("rename")]
        public IActionResult Rename([FromBody] RenameRequest request)
        {
            try
            {
                var targetDir = Path.Combine(_rootDirectory, request.Path ?? "");
                string sourcePath = Path.Combine(targetDir, request.OldName);
                string destPath = Path.Combine(targetDir, request.NewName);

                if (!sourcePath.StartsWith(_rootDirectory) || !destPath.StartsWith(_rootDirectory))
                    return BadRequest(new { error = "Invalid path" });

                if (request.Type == "file")
                {
                    if (!System.IO.File.Exists(sourcePath))
                        return BadRequest(new { error = "File does not exist" });
                    System.IO.File.Move(sourcePath, destPath);
                }
                else if (request.Type == "folder")
                {
                    if (!Directory.Exists(sourcePath))
                        return BadRequest(new { error = "Folder does not exist" });
                    if (Directory.Exists(destPath)) return BadRequest(new { error = "Destination folder already exists" });
                    Directory.Move(sourcePath, destPath);
                }
                else return BadRequest(new { error = "Unknown type" });

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
        public class DownloadRequest
        {
            public List<ItemDto> Items { get; set; }
            public string Path { get; set; }
        }

        /// <summary>
        /// Downloads selected files or folders as a ZIP archive.
        /// </summary>
        [HttpPost("download")]
        public IActionResult Download([FromBody] DownloadRequest request)
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var item in request.Items)
                {
                    var fullPath = Path.Combine(_rootDirectory, request.Path ?? "", item.Name);
                    if (item.Type == "folder" && Directory.Exists(fullPath)) AddFolderToZip(archive, fullPath, item.Name);
                    else if (System.IO.File.Exists(fullPath)) archive.CreateEntryFromFile(fullPath, item.Name);
                }
            }
            memoryStream.Position = 0;
            return File(memoryStream.ToArray(), "application/zip", "download.zip");
        }

        void AddFolderToZip(ZipArchive archive, string sourceFolder, string entryName)
        {
            foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceFolder, file);
                archive.CreateEntryFromFile(file, Path.Combine(entryName, relativePath));
            }
        }
    }
}
