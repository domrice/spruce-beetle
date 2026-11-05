/*
 * MIT License
 * 
 * Copyright (c) 2022 Dominik Reisach
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */


using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;


namespace SpruceBeetle
{
    // Reading delimited text files. The old ReadCSV parsed the file with a hardcoded "," and then
    // kept only the first field, which the callers split again by the user's delimiter - so a
    // comma-delimited file lost every column but the first.
    public static class CSV
    {
        // Rows of fields from a delimited text file, blank lines skipped.
        public static List<string[]> ReadRows(string filePath, string delimiter)
        {
            return File.ReadAllLines(filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(new string[] { delimiter }, System.StringSplitOptions.None))
                .ToList();
        }


        // The first count fields of a row as numbers, invariant culture so that a decimal point
        // means the same thing on every machine. False if the row is short or does not parse.
        public static bool TryParseRow(string[] fields, int count, out double[] values)
        {
            values = new double[count];

            if (fields.Length < count)
                return false;

            for (int i = 0; i < count; i++)
                if (!double.TryParse(fields[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return false;

            return true;
        }
    }
}
