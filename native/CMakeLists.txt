cmake_minimum_required(VERSION 3.25)
project(t360_manager C)

add_executable(t360_manager main.c puff.c)
set_target_properties(t360_manager PROPERTIES C_STANDARD 17 C_EXTENSIONS ON)
target_compile_options(t360_manager PRIVATE -Wall -Wextra -Werror)
xsdk_add_xex(t360_manager NAME "T360 ModLoader" VERSION 0.1.0.0)
