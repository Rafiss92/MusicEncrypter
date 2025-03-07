using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Encrypter2
{
    internal class Program
    {
        static bool stopPlayback = false;
        static void Main()
        {
            var volumeLabel = Properties.Settings.Default.Key; // Zmień na etykietę swojego pendrive'a
            var driveLetter = GetDriveLetterByLabel(volumeLabel);

            if (string.IsNullOrEmpty(driveLetter))
            {
                Console.WriteLine("Nie znaleziono pendrive'a z podaną etykietą.");
                Console.ReadKey();
                return;
            }

            var serialNumber = GetDriveSerialNumber(driveLetter);
            if (string.IsNullOrEmpty(serialNumber))
            {
                Console.WriteLine("Nie można odczytać numeru seryjnego pendrive'a.");
                Console.ReadKey();
                return;
            }

            var key = GenerateKeyFromSerial(serialNumber);

            Console.WriteLine("Co chcesz zrobić?");
            Console.WriteLine("1 - Szyfrowanie");
            Console.WriteLine("2 - Wybór i odszyfrowanie pliku");
            Console.WriteLine("3 - Instalacja/aktualizacja na pendrive (umieść wcześniej plik Player.zip w folderze aplikacji)");
            var choice = Console.ReadLine();

            if (choice == "1")
            {
                var folderPath = AppDomain.CurrentDomain.BaseDirectory;
                var audioFiles = Directory.GetFiles(folderPath+ Properties.Settings.Default.Katalog, "*.mp3").Concat(Directory.GetFiles(folderPath + Properties.Settings.Default.Katalog, "*.wav")).ToArray();

                if (audioFiles.Length == 0)
                {
                    Console.WriteLine("Brak plików audio w folderze aplikacji.");
                    Console.ReadKey();
                }
                else
                {
                    foreach (var file in audioFiles)
                    {
                        var encryptedFile = Path.Combine(driveLetter+ Properties.Settings.Default.Katalog, Path.GetFileName(file) + ".enc");
                        EncryptFile(file, encryptedFile, key);
                        Console.WriteLine($"Plik {Path.GetFileName(file)} został zaszyfrowany.");
                    }
                    Console.WriteLine("Wszystkie pliki zaszyfrowane pomyślnie.");
                    Console.ReadKey();
                }
            }
            else if (choice == "2")
            {
                var encryptedFiles = Directory.GetFiles(driveLetter + Properties.Settings.Default.Katalog, "*.enc");
                var encryptedFilesSorted = encryptedFiles
                                            .Select(file => Path.GetFileNameWithoutExtension(file)) // Wyciąganie samej nazwy pliku
                                            .OrderBy(fileName => fileName)          // Sortowanie alfabetyczne
                                            .ToArray(); ;

                if (encryptedFiles.Length == 0)
                {
                    Console.WriteLine("Brak zaszyfrowanych plików na pendrive.");
                    Console.ReadKey();
                    return;
                }

                while(true)
                { 
                    Console.Clear();
                    Console.WriteLine("Dostępne zaszyfrowane pliki:");

                    for (var i = 0; i < encryptedFiles.Length; i++)
                    {
                        Console.WriteLine($"{i + 1}:\t{Path.GetFileName(encryptedFiles[i])}");
                    }

                    Console.Write("\nWybierz numer pliku do odszyfrowania (0 wychodzi z aplikacji): ");
                    if (int.TryParse(Console.ReadLine(), out var selectedIndex) && selectedIndex > 0 && selectedIndex <= encryptedFiles.Length)
                    {
                        var selectedFile = encryptedFiles[selectedIndex - 1];
                        var originalFileName = Path.GetFileNameWithoutExtension(selectedFile);
                        var decryptedData = DecryptFileToMemory(selectedFile, key);
                        Console.WriteLine("\nPlik odszyfrowany w pamięci, teraz go odtworzymy... Naciśnij klawisz ESC aby przerwać.");
                        PlayAudioFromMemory(decryptedData, originalFileName);
                    }
                    else if (selectedIndex == 0)
                    {
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Nieprawidłowy wybór.");
                        Console.ReadKey();
                    }
                }
            }
            else if (choice == "3") // Installation on pendrive
            {
                var appFolder = AppDomain.CurrentDomain.BaseDirectory;
                var sourceZip = Path.Combine(appFolder, "Player.zip");
                var extractPath = @"E:\Player";

                try
                {
                    using (var archive = ZipFile.OpenRead(sourceZip))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            var destinationPath = Path.Combine(extractPath, entry.FullName);

                            if (entry.Name == "") // This is folder
                            {
                                Directory.CreateDirectory(destinationPath);
                            }
                            else
                            {
                                var parentDir = Path.GetDirectoryName(destinationPath);
                                if (!Directory.Exists(parentDir))
                                {
                                    Directory.CreateDirectory(parentDir);
                                }
                                entry.ExtractToFile(destinationPath, overwrite: true);
                            }
                        }
                    }

                    Console.WriteLine("Pliki z ZIP-a zostały skopiowane.");
                    Console.ReadKey();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd: {ex.Message}");
                    Console.ReadKey();
                }
            }
            else
            {
                Console.WriteLine("Nieznana opcja.");
                Console.ReadKey();
            }
        }
        /// <summary>
        /// Function gets drive letter by drive label
        /// </summary>
        /// <param name="label">Drive label</param>
        /// <returns>String with drive letter</returns>
        static string GetDriveLetterByLabel(string label)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.VolumeLabel.Equals(label, StringComparison.OrdinalIgnoreCase))
                {
                    return drive.Name;
                }
            }
            return null;
        }

        static string GetDriveSerialNumber(string driveLetter)
        {
            try
            {
                var query = "SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='" + driveLetter.TrimEnd('\\') + "'";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        return obj["VolumeSerialNumber"].ToString();
                    }
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        static string GenerateKeyFromSerial(string serial)
        {
            var keyBytes = Encoding.UTF8.GetBytes(serial.PadRight(16, '0').Substring(0, 16));
            return Convert.ToBase64String(keyBytes);
        }

        static void EncryptFile(string inputFile, string outputFile, string keyString)
        {
            var key = Encoding.UTF8.GetBytes(keyString.Substring(0, 16));
            var iv = Encoding.UTF8.GetBytes("1234567812345678"); // TODO: Make this randomized

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;

                using (var fsInput = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                using (var fsOutput = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                using (var cs = new CryptoStream(fsOutput, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    fsInput.CopyTo(cs);
                }
            }
        }

        static byte[] DecryptFileToMemory(string inputFile, string keyString)
        {
            var key = Encoding.UTF8.GetBytes(keyString.Substring(0, 16));
            var iv = Encoding.UTF8.GetBytes("1234567812345678"); // TODO: Make this randomized

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;

                using (var fsInput = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                using (var msOutput = new MemoryStream())
                using (var cs = new CryptoStream(fsInput, aes.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    cs.CopyTo(msOutput);
                    return msOutput.ToArray();
                }
            }
        }

        static void PlayAudioFromMemory(byte[] audioData, string fileName)
        {
            stopPlayback = false;
            var inputThread = new Thread(() =>
            {
                while (!stopPlayback)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    {
                        stopPlayback = true;
                    }
                }
            });
            inputThread.Start();

            using (var ms = new MemoryStream(audioData))
            {
                using (var outputDevice = new WaveOutEvent())
                {
                    if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var reader = new Mp3FileReader(ms))
                        {
                            outputDevice.Init(reader);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == PlaybackState.Playing && !stopPlayback)
                            {
                                Thread.Sleep(100);
                            }
                        }
                    }
                    else if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var reader = new WaveFileReader(ms))
                        {
                            outputDevice.Init(reader);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == PlaybackState.Playing && !stopPlayback)
                            {
                                Thread.Sleep(100);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Nieobsługiwany format pliku.");
                        Console.ReadKey();
                    }
                }
            }
            inputThread.Join();
        }
    }
}
