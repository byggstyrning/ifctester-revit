cmake_minimum_required (VERSION 3.16)

# ============================================================================
# IfcTester ArchiCAD Add-On CMake Build
# Based on official Graphisoft Browser_Control example (AC29)
# ============================================================================

# Find Python 3 (required for resource compilation)
find_package(Python3 REQUIRED COMPONENTS Interpreter)
message(STATUS "Using Python: ${Python3_EXECUTABLE} (version ${Python3_VERSION})")

function (SetCompilerOptions target)
	target_compile_features (${target} PUBLIC cxx_std_20)  # ArchiCAD 29 requires C++20
	target_compile_options (${target} PUBLIC "$<$<CONFIG:Debug>:-DDEBUG>")
	if (WIN32)
		target_compile_options (${target} PUBLIC /W3 /WX- /wd4996 /Zc:wchar_t-)
	else ()
		target_compile_options (${target} PUBLIC -Wall -Wno-error -fvisibility=hidden
			-Wno-multichar
			-Wno-ctor-dtor-privacy
			-Wno-invalid-offsetof
			-Wno-ignored-qualifiers
			-Wno-reorder
			-Wno-overloaded-virtual
			-Wno-unused-parameter
			-Wno-missing-field-initializers
			-Wno-unknown-pragmas
			-Wno-missing-braces
			-Wno-unused-private-field
			-Wno-return-std-move
			-Wno-unused-value
			-Wno-switch
			-Wno-deprecated
			-Wno-shorten-64-to-32)
	endif ()
endfunction ()

function (AddGSModuleIncludeFolder target folder)
	target_include_directories (${target} PUBLIC "${folder}")
endfunction ()

function (AddGSModuleLinkLibrary target library)	
	if (WIN32)
		file (GLOB libs "${library}/Win/*.lib")
		foreach (lib ${libs})
			target_link_libraries (${target} PUBLIC ${lib})
		endforeach ()
	elseif (APPLE)
		# Mac: Link frameworks
		if (IS_DIRECTORY "${library}")
			# Check if library is itself a .framework folder
			get_filename_component (lib_name "${library}" NAME)
			if ("${lib_name}" MATCHES "\\.framework$")
				# This is a framework - link it directly using full path
				# This is the most reliable way to link frameworks in CMake
				target_link_libraries (${target} PUBLIC "${library}")
			else ()
				# This is a directory containing frameworks
				file (GLOB frameworks "${library}/*.framework")
				foreach (framework ${frameworks})
					target_link_libraries (${target} PUBLIC "${framework}")
				endforeach ()
				# Also check for .a static libraries
				file (GLOB libs "${library}/*.a")
				foreach (lib ${libs})
					target_link_libraries (${target} PUBLIC ${lib})
				endforeach ()
			endif ()
		endif ()
	else ()
		target_link_libraries (${target} PUBLIC ${library})
	endif ()
endfunction ()


set_property (GLOBAL PROPERTY USE_FOLDERS ON)

# Project name - IfcTesterArchiCAD
set (AC_ADDON_NAME "IfcTesterArchiCAD")
set (CMAKE_SUPPRESS_REGENERATION 1)
set (CMAKE_CONFIGURATION_TYPES Debug;Release;RelWithDebInfo)

if (WIN32)
	# Only set toolset for Visual Studio generators (not Ninja)
	if (CMAKE_GENERATOR MATCHES "Visual Studio")
		set (CMAKE_GENERATOR_TOOLSET v143)  # ArchiCAD 29 requires VS2022 (v143)
	endif ()
	set (CMAKE_MSVC_RUNTIME_LIBRARY MultiThreadedDLL)
else ()
	set (CMAKE_OSX_DEPLOYMENT_TARGET 10.15)
	if (CMAKE_GENERATOR STREQUAL Xcode)
		set (CMAKE_OSX_ARCHITECTURES "$(ARCHS_STANDARD)" CACHE INTERNAL "" FORCE)
		set (CMAKE_XCODE_ATTRIBUTE_ONLY_ACTIVE_ARCH "NO")
		# Disable automatic code signing for development builds
		# The .whl files in WebApp cannot be signed, so we disable signing entirely
		# For distribution, sign manually after build or use ad-hoc signing
		set (CMAKE_XCODE_ATTRIBUTE_CODE_SIGNING_REQUIRED "NO")
		set (CMAKE_XCODE_ATTRIBUTE_CODE_SIGN_IDENTITY "")
		set (CMAKE_XCODE_ATTRIBUTE_CODE_SIGNING_ALLOWED "NO")
	endif ()
endif ()

# DevKit path - can be overridden via -DAC_API_DEVKIT_DIR=...
if (NOT DEFINED AC_API_DEVKIT_DIR)
	if (DEFINED ENV{ARCHICAD_API_DEVKIT})
		set (AC_API_DEVKIT_DIR "$ENV{ARCHICAD_API_DEVKIT}")
	else ()
		# Default paths to check - platform specific
		if (WIN32)
			set (POSSIBLE_DEVKIT_PATHS
				"C:/code/archicad-api/API.Development.Kit.WIN.29.3100"
				"C:/Program Files/GRAPHISOFT/API Development Kit 29"
			)
		elseif (APPLE)
			set (POSSIBLE_DEVKIT_PATHS
				"$ENV{HOME}/Library/Application Support/GRAPHISOFT/API Development Kit 29"
				"/Applications/GRAPHISOFT/API Development Kit 29"
				"/Users/Shared/GRAPHISOFT/API Development Kit 29"
			)
		endif ()
		foreach (path ${POSSIBLE_DEVKIT_PATHS})
			if (EXISTS "${path}/Support/Inc/ACAPinc.h")
				set (AC_API_DEVKIT_DIR "${path}")
				break ()
			endif ()
		endforeach ()
	endif ()
endif ()

set (AC_API_DEVKIT_DIR "${AC_API_DEVKIT_DIR}" CACHE PATH "API DevKit directory.")
set (AC_ADDON_LANGUAGE "INT" CACHE STRING "Add-On language code.")

message (STATUS "APIDevKit directory: ${AC_API_DEVKIT_DIR}")
set (ACAPINC_FILE_LOCATION ${AC_API_DEVKIT_DIR}/Support/Inc/ACAPinc.h)
if (EXISTS ${ACAPINC_FILE_LOCATION})
	file (READ ${ACAPINC_FILE_LOCATION} ACAPIncContent)
	string (REGEX MATCHALL "#define[ \t]+ServerMainVers_([0-9][0-9])" VersionList ${ACAPIncContent})
	set (ARCHICAD_VERSION ${CMAKE_MATCH_1})
	message (STATUS "Archicad Version: ${ARCHICAD_VERSION}")
else ()
	message (FATAL_ERROR "Failed to detect Archicad version, please check the value of the AC_API_DEVKIT_DIR variable.")
endif ()

if (WIN32)
	add_definitions (-DUNICODE -D_UNICODE -D_ITERATOR_DEBUG_LEVEL=0 -DGS_WIN)
else ()
	add_definitions (-Dmacintosh=1 -DGS_MAC)
endif ()
add_definitions (-DACExtension)

project (${AC_ADDON_NAME})

set (AddOnSourcesFolder ./Src)
set (AddOnResourcesFolder .)

# ============================================================================
# AddOnResources
# ============================================================================

set (ResourceObjectsDir ${CMAKE_BINARY_DIR}/ResourceObjects)

file (GLOB AddOnImageFiles
	${AddOnResourcesFolder}/RFIX/Images/*.svg
)
if (WIN32)
	file (GLOB AddOnResourceFiles
		${AddOnResourcesFolder}/RINT/*.grc
		${AddOnResourcesFolder}/RFIX/*.grc
		${AddOnResourcesFolder}/RFIX.win/*.rc2
	)
else ()
	file (GLOB AddOnResourceFiles
		${AddOnResourcesFolder}/RINT/*.grc
		${AddOnResourcesFolder}/RFIX/*.grc
		${AddOnResourcesFolder}/RFIX.mac/*.plist
	)
endif ()

source_group ("Images" FILES ${AddOnImageFiles})
source_group ("Resources" FILES ${AddOnResourceFiles})
add_custom_target (
	AddOnResources ALL
	DEPENDS "${ResourceObjectsDir}/AddOnResources.stamp"
	SOURCES ${AddOnResourceFiles} ${AddOnImageFiles}
)

get_filename_component (AddOnSourcesFolderAbsolute "${CMAKE_CURRENT_LIST_DIR}/${AddOnSourcesFolder}" ABSOLUTE)
get_filename_component (AddOnResourcesFolderAbsolute "${CMAKE_CURRENT_LIST_DIR}/${AddOnResourcesFolder}" ABSOLUTE)
get_filename_component (APIDevKitToolsFolderAbsolute "${AC_API_DEVKIT_DIR}/Support/Tools" ABSOLUTE)
if (WIN32)
	add_custom_command (
		OUTPUT "${ResourceObjectsDir}/AddOnResources.stamp"
		DEPENDS ${AddOnResourceFiles} ${AddOnImageFiles}
		COMMENT "Compiling resources..."
		COMMAND ${CMAKE_COMMAND} -E make_directory "${ResourceObjectsDir}"
		COMMAND ${Python3_EXECUTABLE} "${APIDevKitToolsFolderAbsolute}/CompileResources.py" "${AC_ADDON_LANGUAGE}" "${AC_API_DEVKIT_DIR}" "${AddOnSourcesFolderAbsolute}" "${AddOnResourcesFolderAbsolute}" "${ResourceObjectsDir}" "${ResourceObjectsDir}/${AC_ADDON_NAME}.res"
		COMMAND ${CMAKE_COMMAND} -E touch "${ResourceObjectsDir}/AddOnResources.stamp"
	)
else ()
	# Mac: Use generator expression for config-dependent paths
	add_custom_command (
		OUTPUT "${ResourceObjectsDir}/AddOnResources.stamp"
		DEPENDS ${AddOnResourceFiles} ${AddOnImageFiles}
		COMMENT "Compiling resources..."
		COMMAND ${CMAKE_COMMAND} -E make_directory "${ResourceObjectsDir}"
		COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/$<CONFIG>/${AC_ADDON_NAME}.bundle/Contents/Resources"
		COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/$<CONFIG>/${AC_ADDON_NAME}.bundle/Contents"
		COMMAND ${Python3_EXECUTABLE} "${APIDevKitToolsFolderAbsolute}/CompileResources.py" "${AC_ADDON_LANGUAGE}" "${AC_API_DEVKIT_DIR}" "${AddOnSourcesFolderAbsolute}" "${AddOnResourcesFolderAbsolute}" "${ResourceObjectsDir}" "${CMAKE_BINARY_DIR}/$<CONFIG>/${AC_ADDON_NAME}.bundle/Contents/Resources"
		COMMAND ${CMAKE_COMMAND} -E copy "${AC_API_DEVKIT_DIR}/Support/Inc/PkgInfo" "${CMAKE_BINARY_DIR}/$<CONFIG>/${AC_ADDON_NAME}.bundle/Contents/PkgInfo"
		COMMAND ${CMAKE_COMMAND} -E touch "${ResourceObjectsDir}/AddOnResources.stamp"
	)
endif ()

# ============================================================================
# AddOn Target
# ============================================================================

file (GLOB AddOnHeaderFiles
	${AddOnSourcesFolder}/*.h
	${AddOnSourcesFolder}/*.hpp
)
file (GLOB AddOnSourceFiles
	${AddOnSourcesFolder}/*.c
	${AddOnSourcesFolder}/*.cpp
)
file (GLOB AllCFiles
	${AddOnSourcesFolder}/*.c
)
set_source_files_properties(${AllCFiles} PROPERTIES LANGUAGE CXX)
set (
	AddOnFiles
	${AddOnHeaderFiles}
	${AddOnSourceFiles}
)
source_group ("Sources" FILES ${AddOnFiles})
if (WIN32)
	add_library (AddOn SHARED ${AddOnFiles})
else ()
	add_library (AddOn MODULE ${AddOnFiles})
endif ()

set_target_properties (AddOn PROPERTIES OUTPUT_NAME ${AC_ADDON_NAME})
if (WIN32)
	set_target_properties (AddOn PROPERTIES SUFFIX ".apx")
else ()
	set_target_properties (AddOn PROPERTIES BUNDLE TRUE)
	set_target_properties (AddOn PROPERTIES MACOSX_BUNDLE_INFO_PLIST "${CMAKE_CURRENT_LIST_DIR}/${AddOnResourcesFolder}/RFIX.mac/Info.plist")
	set_target_properties (AddOn PROPERTIES LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/$<CONFIG>")
endif ()

if (WIN32)
	target_link_options (AddOn PUBLIC "${ResourceObjectsDir}/${AC_ADDON_NAME}.res")
	target_link_options (AddOn PUBLIC /export:GetExportedFuncAddrs,@1 /export:SetImportedFuncAddrs,@2)
	# Additional Windows libraries
	target_link_libraries (AddOn PUBLIC ws2_32.lib)
endif ()

target_include_directories (AddOn PUBLIC
	${AddOnSourcesFolder}
	${AC_API_DEVKIT_DIR}/Support/Inc
)

if (WIN32)
	target_link_libraries (AddOn PUBLIC
		"${AC_API_DEVKIT_DIR}/Support/Lib/ACAP_STAT.lib"
	)
else ()
	find_library (CocoaFramework Cocoa)
	target_link_libraries (AddOn PUBLIC
		"${AC_API_DEVKIT_DIR}/Support/Lib/libACAP_STAT.a"
		${CocoaFramework}
	)
endif ()

SetCompilerOptions (AddOn)
add_dependencies (AddOn AddOnResources)

get_filename_component (APIDevKitModulesDir "${AC_API_DEVKIT_DIR}/Support/Modules" ABSOLUTE)

file (GLOB GSIncludeFolders
	${APIDevKitModulesDir}/*
)
foreach (includeFolder ${GSIncludeFolders})
	AddGSModuleIncludeFolder (AddOn ${includeFolder})
endforeach ()

if (WIN32)
	set (APIDevKitLinkLibDir "${APIDevKitModulesDir}")
else ()
	get_filename_component (APIDevKitLinkLibDir "${AC_API_DEVKIT_DIR}/Support/Frameworks" ABSOLUTE)
endif ()

file (GLOB GSLinkLibFolders
	${APIDevKitLinkLibDir}/*
)
foreach (linkLibFolder ${GSLinkLibFolders})
	AddGSModuleLinkLibrary (AddOn ${linkLibFolder})
endforeach ()

# ============================================================================
# Forced Includes (Required for AC29)
# GSNew.hpp and GSMalloc.hpp must be included before any other headers
# ============================================================================

get_filename_component(new_hpp "${APIDevKitModulesDir}/GSRoot/GSNew.hpp" REALPATH)
get_filename_component(malloc_hpp "${APIDevKitModulesDir}/GSRoot/GSMalloc.hpp" REALPATH)
if(CMAKE_CXX_COMPILER_ID STREQUAL "MSVC")
    target_compile_options(
        AddOn PRIVATE
        "SHELL:/FI \"${new_hpp}\""
        "SHELL:/FI \"${malloc_hpp}\""
    )
elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang$")
    target_compile_options(
        AddOn PRIVATE
        "SHELL:-include \"${new_hpp}\""
        "SHELL:-include \"${malloc_hpp}\""
    )
else()
    message(FATAL_ERROR "Unknown compiler ID.")
endif()

# ============================================================================
# Post-build: Copy WebApp files
# ============================================================================

if (WIN32)
	add_custom_command (TARGET AddOn POST_BUILD
		COMMENT "Copying WebApp files..."
		COMMAND ${CMAKE_COMMAND} -E make_directory "$<TARGET_FILE_DIR:AddOn>/WebApp"
		COMMAND ${CMAKE_COMMAND} -E copy_directory "${CMAKE_CURRENT_LIST_DIR}/../web/dist" "$<TARGET_FILE_DIR:AddOn>/WebApp"
	)
else ()
	# Mac: Copy to bundle Resources directory (using bundle root, not TARGET_FILE_DIR which is MacOS/)
	# $<TARGET_BUNDLE_DIR:AddOn> gives us the .bundle root directory
	add_custom_command (TARGET AddOn POST_BUILD
		COMMENT "Copying WebApp files..."
		COMMAND ${CMAKE_COMMAND} -E make_directory "$<TARGET_BUNDLE_DIR:AddOn>/Contents/Resources/WebApp"
		COMMAND ${CMAKE_COMMAND} -E copy_directory "${CMAKE_CURRENT_LIST_DIR}/../web/dist" "$<TARGET_BUNDLE_DIR:AddOn>/Contents/Resources/WebApp"
	)
endif ()

