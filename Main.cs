/*
Author: Richard Leszczynski
Email: richard@makerdyne.com
Website: http://www.makerdyne.com
*/

using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Security.AccessControl;


namespace RecursiveChecksummer
{
	class MainClass
	{
		// Source and destination root directories
		static string rootsource = null;
		static string rootdest = null;
		static string fileWithChecksums;
		static bool useTemporaryFileWithChecksums = false;
		static ushort numCores = 0;
		static StreamWriter fwcWriter;
		static StreamReader fwcReader;
		static string tmpdir;
		static bool tmpdirpExists = true;
		static ushort programMode = 0;


		public static int Main(string[] args)
		{
			
			/* PROCESS COMMAND LINE ARGUMENTS
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
					if(rootsource != null) {
						Console.WriteLine("ERROR: multiple source (-s) directories have been specified. Please only specify a single source directory");
						printUsageInformation();
						return 1;
					}
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: Please specify source (-s) and/or destination (-d) directories before specifying a file to hold checksums");
						printUsageInformation();
						return 1;
					}
					rootsource = args[++i];
					if(!rootsource.StartsWith("/"))	// Deal with relative directory paths in the command line args
						rootsource = Environment.CurrentDirectory + "/" + rootsource;
					if(!Directory.Exists(rootsource)) {
						Console.WriteLine("ERROR: Source (-s) directory does not exist!");
						return 1;
					}
					break;
				case "-d":
					// signifies that the next CLA will be the root destination directory
					if(rootdest != null) {
						Console.WriteLine("ERROR: multiple destination (-d) directories have been specified. Please only specify a single destination directory");
						printUsageInformation();
						return 1;
					}
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: Please specify source (-s) and/or destination (-d) directories before specifying a file to hold checksums");
						printUsageInformation();
						return 1;
					}
					rootdest = args[++i];
					if(!rootdest.StartsWith("/"))	// Deal with relative directory paths in the command line args
						rootdest = Environment.CurrentDirectory + "/" + rootdest;
					if(!Directory.Exists(rootdest)) {
						Console.WriteLine("ERROR: Destination (-d) directory does not exist!");
						return 1;
					}
					break;
				case "-f":
					/* signifies that the next CLA will be a file containing checksums of files
					 * MODES OF OPERATION:
					 * 1. if -s is already specified, -f signifies a non-existant file which is to be created to hold the checksums of all files in rootsource
					 * 2. if -s and -d are both already specified, -f MAY be used to signify a non-existant file which is to be created to hold the checksums of all
					 *    files in rootsource with will then be checked against all files in destsource. If -f is not specified in this case, a temporary file is created
					 *    for use and deleted before the program closes
					 * 3. if -d is already specified, -f signifies an existing file which contains the checksums that all files in destsource are to be checked against
					 * TODO: decide whether to keep the second option
					 */
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: multiple files to hold checksums (-f) have been specified. Please only specify a single file");
						printUsageInformation();
						return 1;
					}
					fileWithChecksums = args[++i];
					if(!fileWithChecksums.StartsWith("/"))	// Deal with relative directory paths in the command line args
						fileWithChecksums = Environment.CurrentDirectory + "/" + fileWithChecksums;
					if(!File.Exists(fileWithChecksums)) {
						Console.WriteLine("ERROR: File to hold checksums (-f) does not exist!");
						return 1;
					}
					break;
				case "-c":
					// determines the number of concurrent instances of md5sum to run. Cannot be greater than the number of processor cores
					ushort numCoresAvailable = (ushort)Environment.ProcessorCount;
					if(numCoresAvailable<1)
						numCoresAvailable = 1;
					try {
						numCores = Convert.ToUInt16(args[++i]);
					}
					catch(Exception ex) {
						Console.WriteLine("ERROR: Problem with the value specified for the number of cores (-c) argument: {0}", args[i]);
						Console.WriteLine(ex.Message);
						return 1;
					}
					if(numCores == 0)
						numCores=1;
					else if(numCores > Environment.ProcessorCount) {
						numCores = (ushort)Environment.ProcessorCount;
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
						
			// PRE-RUN CHECKS
			// Check that md5sum exists
			if(!File.Exists(@"/bin/md5sum")) {
				Console.WriteLine("ERROR: The program which calculates the checksums (md5sum) either does not exist or cannot be found");
				return 1;
			}
			// Check /tmp or /var/tmp exists and that write permissions are available
			if(Directory.Exists(@"/tmp/"))
				tmpdir = @"/tmp/";
			else if(Directory.Exists(@"/var/tmp/"))
				tmpdir = @"/var/tmp/";
			else {
				Console.WriteLine("ERROR: No temporary directory is available for writing working files to");
				return 1;
			}
			string testFile = tmpdir + "RecursiveChecksummerTestFile.txt";
			try {
				File.Create(testFile);
			}
			catch(Exception ex) {
				Console.WriteLine("ERROR: Cannot create temporary working files in {0}", tmpdir);
				Console.WriteLine(ex.Message);
				return 1;
			}
			File.Delete(testFile);
			
			// DETERMINE IF PROGRAM IS TO RUN IN MODE 1,2 or 3
			// Determine Mode 1
			if((rootsource != null) && (rootdest == null)) {
				if(fileWithChecksums != null)
					programMode = 1;
				else {
					Console.WriteLine("ERROR: A file to hold checksums (-f) must be specified");
					printUsageInformation();
					return 1;
				}
			}
			// Determine Mode 2
			else if((rootsource != null) && (rootdest != null)) {
				programMode = 2;
				if(fileWithChecksums == null) {
					useTemporaryFileWithChecksums = true;
					fileWithChecksums = tmpdir + "fileWithChecksums.txt";
					Console.WriteLine("New location for the file with checksums is {0}", fileWithChecksums);
				}
				else
					useTemporaryFileWithChecksums = false;
			}
			// Determine Mode 3
			else if((rootsource == null) && (rootdest != null)) {
				if(fileWithChecksums != null)
					programMode = 3;
				else {
					Console.WriteLine("ERROR: A file to hold checksums (-f) must be specified");
					printUsageInformation();
					return 1;
				}
			}
			// Determine insufficient arguments for any mode
			else if((rootsource == null) && (rootdest == null)) {
				Console.WriteLine("Error: Neither a source (-s) nor destination (-d) directory has been specified");
				printUsageInformation();
				return 1;
			}
			
			// Create streams required for modes
			try {
				if((programMode == 1) || (programMode == 2))
					// NB: the streamreader required for the 2nd half of the operation in Mode 2 can only be created later
					fwcWriter = new StreamWriter(fileWithChecksums, false, Encoding.Default);
				else if(programMode == 3)
					fwcReader = new StreamReader(fileWithChecksums, Encoding.Default);
				else
					Console.WriteLine("ERROR: An unidentified program mode has been selected. programMode = {0}", programMode);
			}
			catch(Exception ex) {
				Console.WriteLine("ERROR: Cannot access file {0] to hold checksums in {1}",
				                  Path.GetFileName(fileWithChecksums), Path.GetDirectoryName(fileWithChecksums));
				Console.WriteLine(ex.Message);
				return 1;
			}
			
			// TODO: solve issue of relative directory paths...
			Console.WriteLine("Current working directory is {0}", Environment.CurrentDirectory);
			Console.WriteLine("Source directory is {0}", rootsource);
			Console.WriteLine("Destination directory is {0}", rootdest);
			Console.WriteLine("File with checksums is {0}", fileWithChecksums);
			
			// Run a recursive function on the supplied source directory
			// processDirectory(rootsource);
			
			
			return 0;
		}

		static void processDirectory(string directory)
		{
			// create destination directory first
			string destdir = directory.Replace(rootsource, rootdest);
			string[] files = Directory.GetFiles(directory);
			string[] dirs = Directory.GetDirectories(directory);
			
			foreach(string file in files) {
				createChecksum(file);
				//total_files++;				
			}
			foreach(string dir in dirs) {
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
