/*
Author: Richard Leszczynski
Email: richard@makerdyne.com
Website: http://www.makerdyne.com
*/

using System;
using System.IO;
using System.Diagnostics;
using System.Text;


namespace RecursiveChecksummer
{
	class MainClass
	{
		// Source and destination root directories
		static string rootsource;
		static string rootdest;
		static string tmpdir;
		static bool tmpdirpExists = true;
		
		
		public static int Main (string[] args)
		{
			Console.WriteLine ("Recursive Checksummer");
			
			// TODO: process command line args...
			
			// Check that md5sum exists
			if(!File.Exists(@"/bin/md5sum"))
			{
				Console.WriteLine("ERROR: md5sum either does not exist or cannot be found");
				return 1;
			}
			
			// Check source and destination directories exist
			if(!Directory.Exists(rootsource))
			{
				Console.WriteLine("ERROR: Source directory does not exist!");
				return 1;
			}
			if(!Directory.Exists(rootdest))
			{
				Console.WriteLine("ERROR: Destination directory does not exist!");
				return 1;
			}
			
			// Check /tmp or /var/tmp exists and that write permissions are available
			if(Directory.Exists(@"/tmp/"))
			{
				tmpdir = @"/tmp/";
			}
			else if(Directory.Exists(@"/var/tmp/"))
			{
				tmpdir = @"/var/tmp/";
			}
			else
			{
				Console.WriteLine("ERROR: No temporary directory is available for writing working files to");
				return 1;
			}
			string testFile = tmpdir + "RecursiveChecksummerTestFile.txt";
			if(File.Exists(testFile))
			{
				try
				{
					File.Delete(testFile);
				}
				catch(UnauthorizedAccessException UAex)
				{
					Console.WriteLine("ERROR: Problem with file permissions in the tempoarary directory. Cannot create/delete temporary files in " + tmpdir);
					return 1;
				}
				// TODO: dop I need do catch NotSupported exception?
			}
			else
			{
				try
				{
					File.Create(testFile);
				}
				catch(UnauthorizedAccessException UAex)
				{
					Console.WriteLine("ERROR: Problem with file permissions in the tempoarary directory. Cannot create/delete temporary files in " + tmpdir);
					return 1;
				}
				// TODO: dop I need do catch NotSupported exception?
				File.Delete(testFile);
			}
			
			// Run a recursive function on the supplied source directory
			processDirectory(rootsource);
			
			return 0;
		}
		
		static void processDirectory(string directory)
		{
			// create destination directory first
			string destdir = directory.Replace(rootsource,rootdest);
			string[] files = Directory.GetFiles(directory);
			string[] dirs = Directory.GetDirectories(directory);
			
			foreach(string file in files)
			{
				createChecksum(file);
				//total_files++;				
			}
			foreach(string dir in dirs)
			{
				processDirectory(dir);
				//total_dirs++;
			}
		}
		
		static void createChecksum(string file)
		{
			string command = "md5sum";
			Process checksummer = new Process();
			checksummer.StartInfo.FileName = command;
			// TODO: change commandArgs, this is just a placeholder
			string commandArgs = String.Format("{0} {1}", rootdest, rootsource);
			checksummer.StartInfo.Arguments = commandArgs;
			checksummer.StartInfo.CreateNoWindow = true;
			// TODO: change working directory - need md5sum to work from the rootsource directory
			checksummer.StartInfo.WorkingDirectory = "/home/richard/";
			StreamReader md5sumOutput = checksummer.StandardOutput;
			checksummer.Start();
			checksummer.WaitForExit();
			checksummer.Close();
			
			// TODO: Add md5sumOutput to textfile
			
			md5sumOutput.Close();
		}
		
		static void checkChecksum(string file)
		{
			// Stuff
		}
	}
}