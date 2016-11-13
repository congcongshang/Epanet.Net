/*
 * Copyright (C) 2012  Addition, Lda. (addition at addition dot pt)
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see http://www.gnu.org/licenses/.
 */

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Xml.Serialization;
using org.addition.epanet.util;

namespace org.addition.epanet.network.io.input {

public class XMLParser : InputParser {

    private readonly bool gzipped;

    public XMLParser(TraceSource log, bool gzipped):base(log) {
        this.gzipped = gzipped;
    }

    public override Network parse(Network net, string f) {
        this.FileName = Path.GetFullPath(f);

        try {
            Stream @is = this.gzipped ? (Stream)new GZipStream(File.OpenRead(f), CompressionMode.Decompress) : File.OpenRead(f);
            XmlSerializer x = new XmlSerializer(typeof(Network));
            return (Network)x.Deserialize(@is);
        } catch (IOException) {
            throw new ENException(ErrorCode.Err302);
        }
    }
}
}