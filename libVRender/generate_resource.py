# generate_resource.py

input_file_path = "forkawesome-webfont.ttf"
output_header_path = "forkawesome.h"
output_var_name="forkawesome"

# Read the resource file
with open(input_file_path, "rb") as input_file:
    resource_data = input_file.read()

# Generate the header file
with open(output_header_path, "w") as output_header:
    output_header.write("#pragma once\n")
    output_header.write(f"const unsigned char {output_var_name}[] = {{\n")

    for byte in resource_data:
        output_header.write(f"0x{byte:02x}, ")

    output_header.write("\n};\n")
