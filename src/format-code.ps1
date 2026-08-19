#!/bin/bash


echo "Running Code Butler (Reorganizing members)..."
dotnet-code-butler RedStar.slnx

echo "Running dotnet format (Enforcing syntax & spacing)..."
dotnet format

echo "Formatting pipeline complete."