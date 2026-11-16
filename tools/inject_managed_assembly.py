#!/usr/bin/env python3
"""Inject or verify a managed PE embedded inside the Terraria XEX basefile."""
import argparse
import hashlib
import json
import struct
from pathlib import Path

def u16(data, off): return struct.unpack_from("<H", data, off)[0]
def u32(data, off): return struct.unpack_from("<I", data, off)[0]
def sha(data): return hashlib.sha256(data).hexdigest()

def pe_managed_image(data, start):
    if start + 0x40 > len(data) or data[start:start+2] != b"MZ":
        raise ValueError("missing DOS header")
    pe = start + u32(data, start + 0x3C)
    if pe + 24 > len(data) or data[pe:pe+4] != b"PE\0\0":
        raise ValueError("bad PE signature")
    machine = u16(data, pe + 4)
    section_count = u16(data, pe + 6)
    optional_size = u16(data, pe + 20)
    optional = pe + 24
    if machine != 0x014C or u16(data, optional) != 0x010B:
        raise ValueError("not x86 PE32")
    if optional + optional_size > len(data) or u32(data, optional + 92) <= 14:
        raise ValueError("truncated PE or no CLR directory")
    clr_rva, clr_size = struct.unpack_from("<II", data, optional + 96 + 14 * 8)
    if not clr_rva or clr_size < 0x48:
        raise ValueError("invalid CLR directory")
    section_table = optional + optional_size
    sections = []
    max_raw_end = u32(data, optional + 60)
    for i in range(section_count):
        s = section_table + i * 40
        if s + 40 > len(data):
            raise ValueError("truncated PE section table")
        name = data[s:s+8].split(b"\0", 1)[0].decode("ascii", "replace")
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from("<IIII", data, s + 8)
        if raw_pointer + raw_size > len(data) - start:
            raise ValueError(f"section {name} exceeds containing file")
        sections.append((virtual_size, virtual_address, raw_size, raw_pointer))
        max_raw_end = max(max_raw_end, raw_pointer + raw_size)

    def map_rva(rva):
        for virtual_size, virtual_address, raw_size, raw_pointer in sections:
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                delta = rva - virtual_address
                if delta >= raw_size:
                    raise ValueError("CLR RVA points outside raw section")
                return start + raw_pointer + delta
        raise ValueError("CLR RVA not mapped")

    clr_off = map_rva(clr_rva)
    metadata_rva, metadata_size = struct.unpack_from("<II", data, clr_off + 8)
    metadata_off = map_rva(metadata_rva)
    if data[metadata_off:metadata_off+4] != b"BSJB" or metadata_size <= 0:
        raise ValueError("CLR metadata signature/size invalid")
    if start + max_raw_end > len(data):
        raise ValueError("managed PE extends past containing image")
    return {"offset":start,"size":max_raw_end,"machine":machine,
            "clr_rva":clr_rva,"metadata_rva":metadata_rva,"metadata_size":metadata_size}

def find_managed(data):
    found=[]
    pos=0
    while True:
        pos=data.find(b"MZ",pos)
        if pos<0: break
        try: found.append(pe_managed_image(data,pos))
        except (ValueError, struct.error): pass
        pos+=2
    if len(found)!=1:
        raise ValueError(f"expected exactly one embedded managed x86 PE, found {len(found)}")
    return found[0]

def main():
    parser=argparse.ArgumentParser()
    sub=parser.add_subparsers(dest="mode",required=True)
    inj=sub.add_parser("inject")
    inj.add_argument("basefile",type=Path)
    inj.add_argument("patched_dll",type=Path)
    inj.add_argument("output_basefile",type=Path)
    inj.add_argument("--report",type=Path,required=True)
    ver=sub.add_parser("verify")
    ver.add_argument("basefile",type=Path)
    ver.add_argument("expected_dll",type=Path)
    ver.add_argument("--report",type=Path,required=True)
    a=parser.parse_args()

    base=a.basefile.read_bytes()
    loc=find_managed(base)
    extracted=base[loc["offset"]:loc["offset"]+loc["size"]]
    if a.mode=="inject":
        patched=a.patched_dll.read_bytes()
        pinfo=find_managed(patched)
        if pinfo["offset"]!=0 or pinfo["size"]!=len(patched):
            raise SystemExit("patched DLL must be a self-contained PE with no overlay")
        if len(patched)>loc["size"]:
            raise SystemExit(f"patched DLL ({len(patched)}) exceeds reserved embedded span ({loc['size']})")
        if loc["offset"]+loc["size"]>len(base):
            raise SystemExit("embedded DLL span exceeds original basefile")
        output=bytearray(base)
        output[loc["offset"]:loc["offset"]+loc["size"]]=patched+bytes(loc["size"]-len(patched))
        out=bytes(output)
        out_loc=find_managed(out)
        out_bytes=out[out_loc["offset"]:out_loc["offset"]+out_loc["size"]]
        if out_loc["offset"]!=loc["offset"] or out_bytes!=patched:
            raise SystemExit("post-injection embedded DLL verification failed")
        a.output_basefile.parent.mkdir(parents=True,exist_ok=True)
        a.output_basefile.write_bytes(out)
        report={"status":"managed-assembly-injection-pass","mode":"separate-basefile-copy",
                "source_basefile_sha256":sha(base),"output_basefile_sha256":sha(out),
                "embedded_offset":loc["offset"],"reserved_span":loc["size"],
                "original_embedded_sha256":sha(extracted),"patched_dll_sha256":sha(patched),
                "patched_dll_size":len(patched),"basefile_size_preserved":len(out)==len(base),
                "source_basefile_overwritten":False,"output_reopened_and_verified":True}
    else:
        expected=a.expected_dll.read_bytes()
        if loc["offset"]!=0 and extracted!=expected:
            raise SystemExit("rebuilt basefile embedded managed DLL does not match expected patched DLL")
        if loc["offset"]==0:
            raise SystemExit("expected embedded assembly at nonzero offset")
        report={"status":"managed-assembly-embed-verify-pass","embedded_offset":loc["offset"],
                "embedded_size":len(extracted),"expected_size":len(expected),
                "embedded_sha256":sha(extracted),"expected_sha256":sha(expected),
                "exact_match":extracted==expected}
        if extracted!=expected:
            raise SystemExit("embedded managed assembly differs from expected patched DLL")
    a.report.parent.mkdir(parents=True,exist_ok=True)
    a.report.write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(report,indent=2))

if __name__=="__main__": main()
