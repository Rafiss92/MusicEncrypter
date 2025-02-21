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
            string volumeLabel = Properties.Settings.Default.Key; // Zmień na etykietę swojego pendrive'a
            string driveLetter = GetDriveLetterByLabel(volumeLabel);

            if (string.IsNullOrEmpty(driveLetter))
            {
                Console.WriteLine("Nie znaleziono pendrive'a z podaną etykietą.");
                Console.ReadKey();
                return;
            }

            string serialNumber = GetDriveSerialNumber(driveLetter);
            if (string.IsNullOrEmpty(serialNumber))
            {
                Console.WriteLine("Nie można odczytać numeru seryjnego pendrive'a.");
                Console.ReadKey();
                return;
            }

            string key = GenerateKeyFromSerial(serialNumber);

            Console.WriteLine("Co chcesz zrobić?");
            Console.WriteLine("1 - Szyfrowanie");
            Console.WriteLine("2 - Wybór i odszyfrowanie pliku");
            Console.WriteLine("3 - Instalacja/aktualizacja na pendrive (umieść plik Player.zip w folderze aplikacji)");
            string choice = Console.ReadLine();

            if (choice == "1")
            {
                string folderPath = AppDomain.CurrentDomain.BaseDirectory;
                string[] audioFiles = Directory.GetFiles(folderPath+ Properties.Settings.Default.Katalog, "*.mp3").Concat(Directory.GetFiles(folderPath + Properties.Settings.Default.Katalog, "*.wav")).ToArray();

                if (audioFiles.Length == 0)
                {
                    Console.WriteLine("Brak plików audio w folderze aplikacji.");
                    Console.ReadKey();
                }
                else
                {
                    foreach (string file in audioFiles)
                    {
                        string encryptedFile = Path.Combine(driveLetter+ Properties.Settings.Default.Katalog, Path.GetFileName(file) + ".enc");
                        EncryptFile(file, encryptedFile, key);
                        Console.WriteLine($"Plik {Path.GetFileName(file)} został zaszyfrowany.");
                    }
                    Console.WriteLine("Wszystkie pliki zaszyfrowane pomyślnie.");
                    Console.ReadKey();
                }
            }
            else if (choice == "2")
            {
                string[] encryptedFiles = Directory.GetFiles(driveLetter + Properties.Settings.Default.Katalog, "*.enc");
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

                    for (int i = 0; i < encryptedFiles.Length; i++)
                    {
                        Console.WriteLine($"{i + 1}:\t{Path.GetFileName(encryptedFiles[i])}");
                    }

                    Console.Write("\nWybierz numer pliku do odszyfrowania (0 wychodzi z aplikacji): ");
                    if (int.TryParse(Console.ReadLine(), out int selectedIndex) && selectedIndex > 0 && selectedIndex <= encryptedFiles.Length)
                    {
                        string selectedFile = encryptedFiles[selectedIndex - 1];
                        string originalFileName = Path.GetFileNameWithoutExtension(selectedFile);
                        byte[] decryptedData = DecryptFileToMemory(selectedFile, key);
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
                string appFolder = AppDomain.CurrentDomain.BaseDirectory;
                string sourceZip = Path.Combine(appFolder, "Player.zip");
                //string sourceZip = @"C:\ścieżka\do\archiwum.zip";
                string destinationZip = @"E:\Player.zip";
                string extractPath = @"E:\Player";

                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(sourceZip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destinationPath = Path.Combine(extractPath, entry.FullName);

                            if (entry.Name == "") // To jest folder
                            {
                                Directory.CreateDirectory(destinationPath);
                            }
                            else
                            {
                                string parentDir = Path.GetDirectoryName(destinationPath);
                                if (!Directory.Exists(parentDir))
                                {
                                    Directory.CreateDirectory(parentDir);
                                }
                                entry.ExtractToFile(destinationPath, overwrite: true);
                            }
                        }
                    }

                    Console.WriteLine("Pliki z ZIP-a zostały skopiowane.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Nieznana opcja.");
                Console.ReadKey();
            }
        }

        static string GetDriveLetterByLabel(string label)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
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
                string query = "SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='" + driveLetter.TrimEnd('\\') + "'";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                using (ManagementObjectCollection results = searcher.Get())
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
            byte[] keyBytes = Encoding.UTF8.GetBytes(serial.PadRight(16, '0').Substring(0, 16));
            return Convert.ToBase64String(keyBytes);
        }

        static void EncryptFile(string inputFile, string outputFile, string keyString)
        {
            byte[] key = Encoding.UTF8.GetBytes(keyString.Substring(0, 16));
            byte[] iv = Encoding.UTF8.GetBytes("1234567812345678");

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;

                using (FileStream fsInput = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                using (FileStream fsOutput = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                using (CryptoStream cs = new CryptoStream(fsOutput, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    fsInput.CopyTo(cs);
                }
            }
        }

        static byte[] DecryptFileToMemory(string inputFile, string keyString)
        {
            byte[] key = Encoding.UTF8.GetBytes(keyString.Substring(0, 16));
            byte[] iv = Encoding.UTF8.GetBytes("1234567812345678");

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;

                using (FileStream fsInput = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                using (MemoryStream msOutput = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(fsInput, aes.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    cs.CopyTo(msOutput);
                    return msOutput.ToArray();
                }
            }
        }

        static void PlayAudioFromMemory(byte[] audioData, string fileName)
        {
            stopPlayback = false;
            Thread inputThread = new Thread(() =>
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

            using (MemoryStream ms = new MemoryStream(audioData))
            {
                using (WaveOutEvent outputDevice = new WaveOutEvent())
                {
                    if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Mp3FileReader reader = new Mp3FileReader(ms))
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
                        using (WaveFileReader reader = new WaveFileReader(ms))
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
