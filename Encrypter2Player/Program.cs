using Encrypter2Player.Properties;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Encrypter2Player
{
    internal class Program
    {
        static bool stopPlayback = false;
        static void Main()
        {

            string volumeLabel = Settings.Default.Key;
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

            string[] encryptedFiles = Directory.GetFiles(driveLetter + Settings.Default.Katalog, "*.enc");
            var encryptedFilesSorted = encryptedFiles
                                        .Select(file => Path.GetFileNameWithoutExtension(file))
                                        .OrderBy(fileName => fileName)          // Sorting by name
                                        .ToArray(); ;

            if (encryptedFiles.Length == 0)
            {
                Console.WriteLine("Brak zaszyfrowanych plików na pendrive.");
                Console.ReadKey();
                return;
            }

            while (true) 
            {
                Console.Clear(); // Clean the console
                Console.WriteLine("Dostępne zaszyfrowane pliki:");
                for (int i = 0; i < encryptedFiles.Length; i++)
                {
                    Console.WriteLine($"{i + 1}: {Path.GetFileName(encryptedFiles[i])}");
                }

                Console.Write("Wybierz numer pliku do odszyfrowania (0 wychodzi z aplikacji): ");
                if (int.TryParse(Console.ReadLine(), out int selectedIndex) && selectedIndex > 0 && selectedIndex <= encryptedFiles.Length)
                {
                    string selectedFile = encryptedFiles[selectedIndex - 1];
                    string originalFileName = Path.GetFileNameWithoutExtension(selectedFile);
                    byte[] decryptedData = DecryptFileToMemory(selectedFile, key);
                    Console.WriteLine("Trwa odtwarzanie... Aby przerwać naciśnij klawisz ESC");
                    PlayAudioFromMemory(decryptedData, originalFileName);
                }
                else if (selectedIndex == 0) 
                    break;
                else
                {
                    Console.WriteLine("Nieprawidłowy wybór.");
                    Console.ReadKey();
                }
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
