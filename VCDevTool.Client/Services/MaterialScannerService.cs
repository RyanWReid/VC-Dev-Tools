using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace VCDevTool.Client.Services
{
    public class MaterialScannerService
    {
        private string _scanCategoryPath;
        
        // Could be moved to configuration or app settings
        private readonly string[] _subfolders = { "00_Raw", "01_Crop_and_Scale", "02_Tile", "03_Designer", "04_Delivery" };
        
        public event EventHandler<string>? ProgressUpdate;
        public event EventHandler<string>? ErrorOccurred;
        
        public string CurrentMaterialName { get; private set; } = string.Empty;
        public string CurrentMaterialPath { get; private set; } = string.Empty;

        public MaterialScannerService()
        {
            // Initialize with default path
            _scanCategoryPath = @"W:\PROJECTS\Scans";
            ValidateScanPath();
        }
        
        public void ConfigureScanPath(string scanPath)
        {
            _scanCategoryPath = scanPath;
            ValidateScanPath();
        }
        
        public string GetScanPath()
        {
            return _scanCategoryPath;
        }
        
        private void ValidateScanPath()
        {
            if (!Directory.Exists(_scanCategoryPath))
            {
                LogError($"Scan category path does not exist: {_scanCategoryPath}");
            }
        }
        
        public List<string> GetFolderCategories()
        {
            try
            {
                if (!Directory.Exists(_scanCategoryPath))
                {
                    LogError($"Scan path does not exist: {_scanCategoryPath}");
                    return new List<string>();
                }
                
                return Directory.GetDirectories(_scanCategoryPath)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => name!)  // Explicit non-null assertion
                    .ToList();
            }
            catch (Exception ex)
            {
                LogError($"Error getting folder categories: {ex.Message}");
                return new List<string>();
            }
        }

        public string GenerateMaterialName(string materialName, string color, string wear, string roughness)
        {
            // Filter out empty components
            var components = new List<string>();
            
            if (!string.IsNullOrWhiteSpace(materialName))
                components.Add(materialName);
                
            if (!string.IsNullOrWhiteSpace(color))
                components.Add(color);
                
            if (!string.IsNullOrWhiteSpace(wear))
                components.Add(wear);
                
            if (!string.IsNullOrWhiteSpace(roughness))
                components.Add(roughness);
            
            // No counter added here - it will be added when creating the actual directory
            return string.Join("_", components);
        }
        
        public bool CheckMaterialNameExists(string category, string baseName)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(baseName))
                return false;
                
            string baseDirectory = Path.Combine(_scanCategoryPath, category);
            if (!Directory.Exists(baseDirectory))
                return false;
                
            string pattern = $@"{Regex.Escape(baseName)}_\d{{2}}";
            var regex = new Regex(pattern);
            
            return Directory.GetDirectories(baseDirectory)
                .Select(Path.GetFileName)
                .Any(dir => regex.IsMatch(dir ?? string.Empty));
        }
        
        public int GetNextAvailableCounter(string category, string baseName)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(baseName))
                return 1;
                
            string baseDirectory = Path.Combine(_scanCategoryPath, category);
            if (!Directory.Exists(baseDirectory))
                return 1;
                
            // Look for existing directories with the same base name and different counters
            string pattern = $@"{Regex.Escape(baseName)}_(\d{{2}})";
            var regex = new Regex(pattern);
            
            var existingCounters = Directory.GetDirectories(baseDirectory)
                .Select(Path.GetFileName)
                .Where(dir => dir != null)
                .Select(dir => {
                    var match = regex.Match(dir ?? string.Empty);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        if (int.TryParse(match.Groups[1].Value, out int counter))
                            return counter;
                    }
                    return -1;
                })
                .Where(counter => counter != -1)
                .ToList();
                
            if (!existingCounters.Any())
                return 1;
                
            return existingCounters.Max() + 1;
        }
        
        public async Task<(bool Success, string MaterialName, string MaterialPath)> CreateMaterialAsync(
            string category, string materialName, string color, string wear, string roughness, bool skipPromptForRename = false)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(materialName))
            {
                LogError("Category and material name are required");
                return (false, string.Empty, string.Empty);
            }
            
            try
            {
                // Check if the category folder exists
                string categoryPath = Path.Combine(_scanCategoryPath, category);
                if (!Directory.Exists(categoryPath))
                {
                    // Category doesn't exist, needs to be created
                    LogProgress($"Category '{category}' doesn't exist in scan path");
                    
                    // This will be replaced with a dialog in the UI
                    var result = System.Windows.MessageBox.Show(
                        $"Category '{category}' doesn't exist in the scan path. Create it?",
                        "Create Category",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);
                        
                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        Directory.CreateDirectory(categoryPath);
                        LogProgress($"Created category folder: {categoryPath}");
                    }
                    else
                    {
                        LogError("Category creation cancelled by user");
                        return (false, string.Empty, string.Empty);
                    }
                }
                
                // Generate base name without counter
                string baseName = GenerateMaterialName(materialName, color, wear, roughness);
                
                // Find the next available counter
                int counter = 1;
                string fullMaterialName = $"{baseName}_{counter:D2}";
                string fullPath = Path.Combine(_scanCategoryPath, category, fullMaterialName);
                
                // Check if the directory already exists
                if (Directory.Exists(fullPath) || CheckMaterialNameExists(category, baseName))
                {
                    // If we're not skipping the prompt, show a confirmation dialog
                    if (!skipPromptForRename)
                    {
                        counter = GetNextAvailableCounter(category, baseName);
                        string newMaterialName = $"{baseName}_{counter:D2}";
                        
                        var result = System.Windows.MessageBox.Show(
                            $"Material name '{baseName}_01' already exists. Use '{newMaterialName}' instead?",
                            "Material Already Exists",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                            
                        if (result == System.Windows.MessageBoxResult.No)
                        {
                            LogError("Material creation cancelled by user");
                            return (false, string.Empty, string.Empty);
                        }
                        
                        LogProgress($"Using alternative name: {newMaterialName}");
                    }
                    else
                    {
                        counter = GetNextAvailableCounter(category, baseName);
                    }
                    
                    fullMaterialName = $"{baseName}_{counter:D2}";
                    fullPath = Path.Combine(_scanCategoryPath, category, fullMaterialName);
                }
                
                // Create main directory
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    LogProgress($"Created directory: {fullPath}");
                }
                
                // Create subfolders
                foreach (var subfolder in _subfolders)
                {
                    string subfolderPath = Path.Combine(fullPath, subfolder);
                    
                    if (!Directory.Exists(subfolderPath))
                    {
                        Directory.CreateDirectory(subfolderPath);
                        LogProgress($"Created subfolder: {subfolderPath}");
                    }
                    
                    // Create special subfolder in Delivery
                    if (subfolder == "04_Delivery")
                    {
                        string deliveryAssetPath = Path.Combine(subfolderPath, fullMaterialName);
                        if (!Directory.Exists(deliveryAssetPath))
                        {
                            Directory.CreateDirectory(deliveryAssetPath);
                            LogProgress($"Created Asset Delivery Folder: {deliveryAssetPath}");
                        }
                    }
                }
                
                // Handle Artengine template for Tile subfolder
                await Task.Run(() => {
                    try
                    {
                        string tilePath = Path.Combine(fullPath, "02_Tile", fullMaterialName);
                        string artengineTemplate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scan_Templates", "MATNAME");
                        
                        // Skip this part if template doesn't exist - we can add it later
                        if (Directory.Exists(artengineTemplate))
                        {
                            // Copy template directory
                            if (!Directory.Exists(tilePath))
                            {
                                CopyDirectory(artengineTemplate, tilePath);
                                LogProgress($"Copied Artengine template to: {tilePath}");
                            }
                            
                            // Update template file
                            string artengineProjectFile = Path.Combine(tilePath, "MATNAME.art");
                            if (File.Exists(artengineProjectFile))
                            {
                                string newArtengineProjectFile = Path.Combine(tilePath, $"{fullMaterialName}.art");
                                
                                // Replace placeholders in content
                                string fullImagePathName = fullPath.Replace("\\", "\\\\");
                                string fullImagePathNameNoColon = fullImagePathName.Replace(":", "");
                                
                                string fileContents = File.ReadAllText(artengineProjectFile);
                                fileContents = fileContents.Replace("MATNAME", fullMaterialName);
                                fileContents = fileContents.Replace("FILEPATH", fullImagePathNameNoColon);
                                fileContents = fileContents.Replace("FILECOLON", fullImagePathName);
                                
                                File.WriteAllText(artengineProjectFile, fileContents);
                                
                                // Rename template file
                                if (artengineProjectFile != newArtengineProjectFile)
                                {
                                    File.Move(artengineProjectFile, newArtengineProjectFile);
                                    LogProgress($"Created Artengine File: {newArtengineProjectFile}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error creating Artengine files: {ex.Message}");
                    }
                });
                
                // Handle Substance Designer template
                await Task.Run(() => {
                    try
                    {
                        string designerPath = Path.Combine(fullPath, "03_Designer");
                        string substanceTemplate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scan_Templates", "MATNAME.sbs");
                        
                        // Skip this part if template doesn't exist - we can add it later
                        if (File.Exists(substanceTemplate))
                        {
                            string substanceFile = Path.Combine(designerPath, "MATNAME.sbs");
                            string newSubstanceFile = Path.Combine(designerPath, $"{fullMaterialName}.sbs");
                            
                            // Copy template file
                            File.Copy(substanceTemplate, substanceFile, true);
                            
                            // Replace placeholders in content
                            string fileContents = File.ReadAllText(substanceFile);
                            fileContents = fileContents.Replace("MATNAME", fullMaterialName);
                            fileContents = fileContents.Replace("FULLSCANPATH", fullPath);
                            
                            File.WriteAllText(substanceFile, fileContents);
                            
                            // Rename template file
                            if (substanceFile != newSubstanceFile)
                            {
                                File.Move(substanceFile, newSubstanceFile);
                                LogProgress($"Created Substance File: {newSubstanceFile}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error creating Substance Designer files: {ex.Message}");
                    }
                });
                
                // Create directory on desktop
                await Task.Run(() => {
                    try
                    {
                        string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fullMaterialName);
                        
                        if (!Directory.Exists(desktopPath))
                        {
                            Directory.CreateDirectory(desktopPath);
                            LogProgress($"Created desktop directory: {desktopPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error creating desktop directory: {ex.Message}");
                    }
                });
                
                // Save current material name and path for later copying
                CurrentMaterialName = fullMaterialName;
                CurrentMaterialPath = fullPath;
                
                return (true, fullMaterialName, fullPath);
            }
            catch (Exception ex)
            {
                LogError($"Error creating material: {ex.Message}");
                return (false, string.Empty, string.Empty);
            }
        }

        public bool CopyMaterialNameToClipboard()
        {
            if (string.IsNullOrEmpty(CurrentMaterialName))
                return false;
                
            try
            {
                System.Windows.Clipboard.SetText(CurrentMaterialName);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error copying material name to clipboard: {ex.Message}");
                return false;
            }
        }
        
        public bool CopyMaterialPathToClipboard()
        {
            if (string.IsNullOrEmpty(CurrentMaterialPath))
                return false;
                
            try
            {
                string rawPath = Path.Combine(CurrentMaterialPath, "00_Raw");
                System.Windows.Clipboard.SetText(rawPath);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error copying material path to clipboard: {ex.Message}");
                return false;
            }
        }
        
        // Asana integration placeholder - will be implemented later
        private async Task CreateAsanaTaskAsync(string materialName, string materialPath)
        {
            // This will be implemented later
            await Task.CompletedTask;
        }
        
        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Create destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destinationDir, fileName);
                File.Copy(file, destFile, true);
            }

            // Copy subdirectories recursively
            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(directory);
                string destDir = Path.Combine(destinationDir, dirName);
                CopyDirectory(directory, destDir);
            }
        }
        
        private void LogProgress(string message)
        {
            ProgressUpdate?.Invoke(this, message);
        }
        
        private void LogError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
        }
    }
} 