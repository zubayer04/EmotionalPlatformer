using System;
using System.Collections.Generic;

[Serializable]
public class ChunkBlueprintValidationResult
{
    public bool isValid = true;
    public List<string> errors = new List<string>();

    public void AddError(string error)
    {
        isValid = false;
        errors.Add(error);
    }
}