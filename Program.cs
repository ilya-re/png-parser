using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;

namespace png_parser
{
	internal class Program {
		[Flags]
		enum PrintingFlags {
			Text = 1,
			Time = 2,
		};
		static readonly byte[] PNG_HEADER = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        static PrintingFlags printing_flags;
        static readonly FrozenDictionary<string, string> chunk_table;
		static readonly string exe_path;
		static Program() {
			// Read chunks' descriptions from the JSON file
			exe_path = Assembly.GetExecutingAssembly().Location;
			string json_path = Path.Combine(Path.GetDirectoryName(exe_path) ?? Path.GetPathRoot(exe_path), "chunk_table.json");
			using FileStream json = new(json_path, FileMode.OpenOrCreate, FileAccess.Read);
			var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
			chunk_table = dict.ToFrozenDictionary();
		}
		static uint ReadUInt32BE(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
		static string PrintNextChunk(FileStream img_file, BinaryReader reader, out bool eof_reached) {
			eof_reached = false;
			string chunk_text_data = string.Empty;
			// Read chunk length as 32-bit unsigned big-endian integer
			uint chunk_length;
			try { chunk_length = ReadUInt32BE(reader); }
			catch (EndOfStreamException) {
				eof_reached = true;
				return string.Empty;
			}
			// Read chunk type as 4 8-bit characters
			string chunk_type = new(reader.ReadChars(4));
			// Print chunk type and length
			Console.Write($"Chunk \"{chunk_type}\", {chunk_length,-5} bytes");
			// Read tIME data if printing enabled
            bool print_text_chunk = chunk_type == "tEXt" && printing_flags.HasFlag(PrintingFlags.Text);
			bool print_time_chunk = chunk_type == "tIME" && printing_flags.HasFlag(PrintingFlags.Time);
            if (print_time_chunk) {
				var year = BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());
				byte month = reader.ReadByte();
				byte day = reader.ReadByte();
				byte hour = reader.ReadByte();
				byte minute = reader.ReadByte();
				byte second = reader.ReadByte();
				chunk_text_data = new DateTime(year, month, day, hour, minute, second).ToString("yyyy-MM-dd HH:mm:ss");
			}
			// Read tEXt data if printing enabled
			else if (print_text_chunk) { chunk_text_data = new(reader.ReadChars((int)chunk_length)); }
			else {
				// Skip to the end of this chunk
				img_file.Seek(chunk_length, SeekOrigin.Current);
			}
			// Read chunk CRC as 32-bit unsigned big-endian integer
			var crc = ReadUInt32BE(reader);
			// Print CRC
			Console.Write($", CRC = {crc:X08} ");
			// Print chunk description
			Console.WriteLine(chunk_table.TryGetValue(chunk_type, out string? chunk_desc) ? $"({chunk_desc})" : string.Empty);
			// Print data from the chunk
			var old_color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			if (print_text_chunk || print_time_chunk) { Console.WriteLine(chunk_text_data); }
			Console.ForegroundColor = old_color;
			return chunk_type;
		}
		static int Main(string[] args) {
			if (args.Length == 0) {
				Console.WriteLine(
"""
Usage: png-parser -i <filename> [--print-text] [--print-time]
-i <filename>: specifies a PNG file
--print-text:  print contents of tEXt chunks (text data)
--print-time:  print contents of tIME chunks (modification time)
""");
				return 0;
			}
			string? filename = null;
			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "-i" && args.Length > i + 1) { filename = args[i + 1]; }
				if (args[i] == "--print-text") { printing_flags |= PrintingFlags.Text; }
				if (args[i] == "--print-time") { printing_flags |= PrintingFlags.Time; }
			}
			if (filename is null) {
				Console.WriteLine("No filename specified.");
				return 1;
			}
			if (Directory.Exists(filename)) {
				Console.WriteLine($"{filename} is a directory, not a file.");
				return 1;
			}
			FileStream img_file;
			try { img_file = new FileStream(filename, FileMode.Open, FileAccess.Read); }
			catch (Exception e) when (e is FileNotFoundException or UnauthorizedAccessException or IOException) {
				Console.WriteLine(e.Message);
				return 1;
			}
			// Try to read the PNG header
			using var reader = new BinaryReader(img_file, System.Text.Encoding.ASCII);
			byte[] file_header = reader.ReadBytes(8);
			// Check if the file is not empty
			if (file_header.Length == 0) {
				Console.WriteLine("The file is empty.");
				return 1;
			}
			// Check if the file is PNG
			if (!file_header.SequenceEqual(PNG_HEADER)) {
				Console.WriteLine("The file does not start with a PNG header.");
				return 1;
			}
			Console.WriteLine("PNG header is valid");
			string chunk_type;
			bool eof;
			do { chunk_type = PrintNextChunk(img_file, reader, out eof); } while (!(eof || chunk_type == "IEND"));
			img_file.Close();
			return 0;
		}
	}
}
