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
		static string rootsource = null;
		static string rootdest = null;
		static string fileWithChecksums;
		static StreamWriter fwcWriter;
		static StreamReader fwcReader;
		static string tmpdir;
		static bool tmpdirpExists = true;


		public static int Main(string[] args)
		{
			
			/* Process command line arguments
			 * Command line flags:
			 * -s = root source directory
			 * -d = root destination directory
			 * -f = path to a file containing checksums of a set of files to check
			 * -h = print program usage details and exit
			 */

Console.WriteLine("Number of command line arguments is " + args.GetLength(0));
			for(uint i = 0; i < args.GetLength(0); i++)
			{
				switch(args[i])
				{
				case "-s":
					// signifies that the next CLA will be the root source directory
					if(!Directory.Exists(rootsource = args[++i]))
					{
						Console.WriteLine("ERROR: Source directory does not exist!");
						return 1;
					}
					break;
				case "-d":
					// signifies that the next CLA will be the root destination directory
					if(!Directory.Exists(rootdest = args[++i]))
					{
						Console.WriteLine("ERROR: Destination directory does not exist!");
						return 1;
					}
					break;
				case "-f":
					/* signifies that the next CLA will be a file containing checksums of files
					 * if -s is already specified, -f signifies a non-existant file which is to be created to hold the checksums of all files in rootsource
					 * TODO: decide whether to keep this second option
					 * if -s and -d are both already specified, -f signifies a non-existant file which is to be created to hold the checksums of all files in rootsource with will then be checked against all files in destsource
					 * if -d is already specified, -f signifies an existing file which contains the checksums that all files in destsource are to be checked against
					 */
					fileWithChecksums = args[++i];
					if((rootdest == null) && (rootdest == null))
					{
						// Error condition, need to have already specified either a source OR destination directory
						Console.WriteLine("ERROR: Please specify EITHER a source (-s) OR destination (-d) directory before specifying a file (-f) of checksums");
						printUsageInformation();
						return 1;
					}
					else
					{
						if(rootsource != null)
						{
							// TODO: create a file to hold checksums and a streamwriter
							fwcWriter = new StreamWriter(fileWithChecksums, false, Encoding.Default);
						}
						else
						{
							// TODO: open fileWithChecksums and create a streamreader
							fwcReader = new StreamReader(fileWithChecksums, Encoding.Default);
						}
					}
					break;
				case "-h":
					// print program usage information and exit
					printUsageInformation();
					return 0;
				default:
					// print program usage information and exit
					printUsageInformation();
					return 1;
				}
			}
			
			
			
			// Check that md5sum exists
			if(!File.Exists(@"/bin/md5sum"))
			{
				Console.WriteLine("ERROR: md5sum either does not exist or cannot be found");
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
					Console.WriteLine(UAex.Message);
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
					Console.WriteLine(UAex.Message);
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
			string destdir = directory.Replace(rootsource, rootdest);
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

		static void printUsageInformation()
		{
			Console.WriteLine("Recursive Checksummer");
			Console.WriteLine("Program to generate or check checksums of all files within a directory tree");
			Console.WriteLine("Usage: ");
		}

		static bool checkDirExists(string dir)
		{
			return true;
		}
		
		static bool checkFileExists(string file)
		{
			return true;
		}
	}
}
