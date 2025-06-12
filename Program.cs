using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;

namespace png_parser
{
	internal class Program {
		static readonly byte[] PNG_HEADER;
		static bool eof;
		static bool do_print_text = false;
		static readonly Dictionary<string, string> chunk_table = [];
		static readonly string exe_path;
		static Program() {
			exe_path = Assembly.GetExecutingAssembly().Location;
			PNG_HEADER = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
			string json_path = Path.Combine(Path.GetDirectoryName(exe_path), "chunk_table.json");
			using FileStream json = new(json_path, FileMode.OpenOrCreate, FileAccess.Read);
			var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
			if (dict != null) { chunk_table = dict; }
		}
		static uint ReadUInt32BE(BinaryReader reader) {
			return BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
		}
		static string ReadChunk(FileStream img_file, BinaryReader reader) {
			uint chunk_length = 0;
			string text_string = string.Empty;
			// Read chunk length as 32-bit unsigned big-endian integer
			try { chunk_length = ReadUInt32BE(reader); }
			catch (EndOfStreamException) { eof = true; }
			// Read chunk type as 4 8-bit characters
			string chunk_type = new(reader.ReadChars(4));
			bool print_text_chunk = chunk_type == "tEXt" && do_print_text;
			// Read tEXt data if printing enabled
			if (print_text_chunk) { text_string = new(reader.ReadChars((int)chunk_length)); }
			else {
				// Advance file position to the chunk end
				img_file.Seek(chunk_length, SeekOrigin.Current);
			}
			// Read chunk CRC as 32-bit unsigned big-endian integer
			var crc = ReadUInt32BE(reader);
			Console.Write($"Chunk \"{chunk_type}\", {chunk_length,-5} bytes, CRC = {crc:X08} ");
			Console.WriteLine(chunk_table.ContainsKey(chunk_type) ? $"({chunk_table[chunk_type]})" : string.Empty);
			// Print text from the tEXt chunk
			if (print_text_chunk) {
				var old_color = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"{text_string}");
				Console.ForegroundColor = old_color;
			}
			return chunk_type;
		}
		static void Main(string[] args) {
			if (args.Length == 0) {
				Console.WriteLine($"Usage: {Path.GetFileNameWithoutExtension(exe_path)} -i <filename> [--print-text]");
				Environment.Exit(0);
			}
			string? filename = null;
			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "-i" && args.Length > i + 1) { filename = args[i + 1]; }
				if (args[i] == "--print-text") { do_print_text = true; }
			}
			if (filename == null) {
				Console.WriteLine("No filename specified.");
				Environment.Exit(1);
			}
			var img_file = new FileStream(filename, FileMode.Open, FileAccess.Read);
			var reader = new BinaryReader(img_file, System.Text.Encoding.ASCII);
			byte[] file_header = reader.ReadBytes(8);
			if (file_header.Length == 0) {
				Console.WriteLine("The file is empty.");
				Environment.Exit(1);
			}
			if (!file_header.SequenceEqual(PNG_HEADER)) {
				Console.WriteLine("The file does not start with a PNG header.");
				Environment.Exit(1);
			}
			Console.WriteLine("PNG header - OK");
			string chunk_type;
			do { chunk_type = ReadChunk(img_file, reader); } while (!(eof || chunk_type == "IEND"));
			reader.Close();
			img_file.Close();
		}
	}
}
