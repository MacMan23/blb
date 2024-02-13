﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public class TileGrid : MonoBehaviour
{
  [System.Serializable]
  public class Element
  {
    public Vector2Int m_GridIndex;
    public TileType m_Type;
    public TileColor m_TileColor;
    public Direction m_Direction;
    [System.NonSerialized]
    public GameObject m_GameObject;
    public List<Vector2Int> m_Path;


    public Element() { }

    public Element(Vector2Int gridIndex, TileState state, GameObject gameObject)
    {
      m_GridIndex = gridIndex;
      m_Type = state.Type;
      m_TileColor = state.Color;
      m_Direction = state.Direction;
      m_Path = state.Path;
      m_GameObject = gameObject;

      if (gameObject.TryGetComponent<ColorCode>(out var colorCode))
        colorCode.m_Element = this;

      if (gameObject.TryGetComponent<TileDirection>(out var tileDirection))
        tileDirection.m_Element = this;
    }

    public bool Equals(Element other)
    {
      if (m_GridIndex != other.m_GridIndex)
        return false;
      if (m_Type != other.m_Type)
        return false;
      if (m_TileColor != other.m_TileColor)
        return false;
      if (m_Direction != other.m_Direction)
        return false;
      // Check if the paths equal if they both exist
      if (other.m_Path != null && ((!m_Path?.SequenceEqual(other.m_Path)) ?? false))
        return false;
      return true;
    }

    public void SetState(TileState state)
    {
      m_Type = state.Type;
      m_TileColor = state.Color;
      m_Direction = state.Direction;
      m_Path = state.Path;
    }

    public TileState ToState()
    {
      return new TileState()
      {
        Type = m_Type,
        Color = m_TileColor,
        Direction = m_Direction,
        Path = m_Path,
      };
    }


    public T GetComponent<T>() where T : Component
    {
      if (m_GameObject == null)
        return null;

      return m_GameObject.GetComponent<T>();
    }
  }


  Dictionary<Vector2Int, Element> m_Grid = new();
  List<KeyValuePair<Vector2Int, Element>> m_GridSaveBuffer;
  // The old grid from on level load. This grid won't change with the editor.
  // This is so we don't have to load the level again when save the changes.
  Dictionary<Vector2Int, Element> m_OldGrid = new();
  public Transform m_BoundsRoot;
  public Transform m_MaskTransform;
  public Transform m_ColoredOutlineTransform;
  public Transform m_DarkOutlineTransform;
  public Camera m_EditorCamera;
  public Outliner m_WorldExtentsOutliner;

  Transform m_Transform;

  Dictionary<TileType, Transform> m_Roots = new();

  TilesPalette m_TilesPalette;
  SpriteMask m_Mask;
  SpriteRenderer m_ColoredOutlineRenderer;
  SpriteRenderer m_DarkOutlineRenderer;

  // The Z depth of objects placed in the grid.
  // NOTE: Is there any potential reason for this to be public...?
  readonly float m_GridZ = 0;

  [HideInInspector]
  public Vector3 m_MinBounds = new(float.MaxValue, float.MaxValue, 0);
  [HideInInspector]
  public Vector3 m_MaxBounds = new(float.MinValue, float.MinValue, 0);

  GameObject m_MostRecentlyCreatedTile;
  List<Vector2Int> m_BatchedIndices;

  private void Awake()
  {
    m_Transform = transform;

    m_MinBounds.z = m_GridZ;
    m_MaxBounds.z = m_GridZ;

    m_TilesPalette = FindObjectOfType<TilesPalette>();

    m_Mask = m_MaskTransform.GetComponent<SpriteMask>();
    m_ColoredOutlineRenderer = m_ColoredOutlineTransform.GetComponent<SpriteRenderer>();
    m_DarkOutlineRenderer = m_DarkOutlineTransform.GetComponent<SpriteRenderer>();
  }


  public void RegisterRoot(TileType tileType, string name)
  {
    if (m_Roots.ContainsKey(tileType))
      return;

    var newRoot = new GameObject(name).transform;
    newRoot.SetParent(transform);
    m_Roots.Add(tileType, newRoot);
  }


  public Element Get(Vector2Int index)
  {
    if (!m_Grid.TryGetValue(index, out Element output))
    {
      output = new Element
      {
        m_GridIndex = index
      };
    }

    return output;
  }


  public bool Check(Vector2Int index)
  {
    return m_Grid.ContainsKey(index);
  }


  void RecomputeBoundsGivenNewIndex(Vector2Int index)
  {
    RecomputeBoundsGivenNewIndexHelper(index);

    SetUpOutline();
  }


  void RecomputeBoundsGivenNewIndexHelper(Vector2Int index)
  {
    if (index.x < m_MinBounds.x)
      m_MinBounds.x = index.x;
    if (index.x > m_MaxBounds.x)
      m_MaxBounds.x = index.x;
    if (index.y < m_MinBounds.y)
      m_MinBounds.y = index.y;
    if (index.y > m_MaxBounds.y)
      m_MaxBounds.y = index.y;
  }


  void RecomputeBoundsGivenRemovalAtIndex(Vector2Int index)
  {
    // i'm using an epsilon here because i'm afraid of spooky
    // floating-point stuff ruining my day. this is safe because
    // our tile size is much larger than this epsilon, so at
    // worst, it's just a tiny tiny bit of extra work

    var greaterThanLeft = index.x > m_MinBounds.x + Mathf.Epsilon;
    var lessThanRight = index.x < m_MaxBounds.x - Mathf.Epsilon;
    var greaterThanBottom = index.y > m_MinBounds.y + Mathf.Epsilon;
    var lessThanTop = index.y < m_MaxBounds.y - Mathf.Epsilon;
    
    if (greaterThanLeft && lessThanRight && greaterThanBottom && lessThanTop)
      return;

    RecomputeBounds();
  }


  public void RecomputeBounds()
  {
    m_MinBounds = new Vector3(float.MaxValue, float.MaxValue, m_GridZ);
    m_MaxBounds = new Vector3(float.MinValue, float.MinValue, m_GridZ);

    foreach (var entry in m_Grid)
      RecomputeBoundsGivenNewIndexHelper(entry.Key);

    SetUpOutline();
  }


  public void SetUpOutline()
  {
    m_WorldExtentsOutliner.OutlineWithBounds(m_MinBounds, m_MaxBounds);
  }


  public void CopyGridBuffer()
  {
    m_GridSaveBuffer = m_Grid.ToList();
  }

  public string ToJsonString()
  {
    var gridStringBuilder = new StringBuilder();

    foreach (var element in m_GridSaveBuffer)
      gridStringBuilder.AppendLine(JsonUtility.ToJson(element.Value));

    return gridStringBuilder.ToString();
  }

  // This should only be called once per save, as we are destorying saved data and overwriting it.
  public string GetDiffrences()
  {
    StringBuilder diffrences = new();
    bool hasDiffrences = false;

    foreach (var kvp in m_GridSaveBuffer)
    {
      Vector2Int position = kvp.Key;
      Element currentElement = kvp.Value;

      if (m_OldGrid.TryGetValue(position, out Element oldElement))
      {
        bool same = currentElement.Equals(oldElement);

        // Removed element so we don't check it again in the next loop
        m_OldGrid.Remove(position);

        if (same)
          continue;
      }
      diffrences.AppendLine(JsonUtility.ToJson(currentElement));
      hasDiffrences = true;
    }

    diffrences.AppendLine("-");

    // Every tile left in the old grid will be removed
    foreach (var kvp in m_OldGrid)
    {
      diffrences.AppendLine($"{{{kvp.Key.x},{kvp.Key.y}}}");
      hasDiffrences = true;
    }

    // Updates old grid to be the "new" grid
    m_OldGrid = m_GridSaveBuffer.ToDictionary(pair => pair.Key, pair => pair.Value);

    if (hasDiffrences)
      return diffrences.ToString();
    return null;
  }

  public void LoadFromDictonary(Dictionary<Vector2Int, Element> grid)
  {
    var startTime = DateTime.Now;

    ForceClearGrid();

    // Create a shallow copy of the dictonary
    m_Grid = grid.ToDictionary(pair => pair.Key, pair => pair.Value);
    m_OldGrid = grid.ToDictionary(pair => pair.Key, pair => pair.Value);

    var successes = 0;
    var failures = 0;

    foreach (var element in m_Grid)
    {
      // Create this tile for both the old and new grid.
      // The new grid will create an object, the old will just have the data.
      try
      {
        var index = element.Value.m_GridIndex;
        var state = element.Value.ToState();

        CreateTile(index, state, false);

        ++successes;
      }
      catch (System.ArgumentException e)
      {
        Debug.LogError($"Failed to create the tile:\n \"{element.Value.ToState()}\" " +
          $"\nas a grid element. {e.Message} ({e.GetType()})");

        ++failures;
      }
    }

    RecomputeBounds();

    PrintLoadErrors(failures, successes, startTime);
  }

  // Loads a file with no undoing.
  public void LoadFromJsonStrings(string[] jsonStrings)
  {
    var startTime = DateTime.Now;

    ForceClearGrid();
    // Clear old grid to use to store the current level data
    //m_OldGrid.Clear();

    var successes = 0;
    var failures = 0;

    foreach (var jsonString in jsonStrings)
    {
      try
      {
        var element = JsonUtility.FromJson<Element>(jsonString);
        var index = element.m_GridIndex;
        var state = element.ToState();

        CreateTile(index, state, false);

        ++successes;
      }
      catch (System.ArgumentException e)
      {
        Debug.LogError($"Failed to parse the line \"{jsonString}\" " +
          $"as a grid element. {e.Message} ({e.GetType()})");

        ++failures;
      }
    }

    // Create a shallow copy of the dictonary
    m_OldGrid = m_Grid.ToDictionary(pair => pair.Key, pair => pair.Value);

    RecomputeBounds();

    PrintLoadErrors(failures, successes, startTime);
  }

  // Create debug output for amount of successes and failures.
  public static void PrintLoadErrors(int failures, int successes, DateTime startTime)
  {
    var thingWord = failures == 1 ? "thing" : "things";
    var failString = $"{failures} {thingWord}";

    if (successes > 0)
    {
      var successWord = successes == 1 ? "success" : "successes";
      var successString = $"{successes} {successWord}";
      var additionalString = "";

      if (failures > 0)
      {
        additionalString = $" (and {failString} we didn't recognize...)";
      }

      var duration = DateTime.Now - startTime;
      var h = duration.Hours; // If this is greater than 0, we got beeg problems
      var m = duration.Minutes;
      var s = Math.Round(duration.TotalSeconds % 60.0, 2);

      var durationStr = "";
      if (h > 0)
        durationStr += $"{h}h ";
      if (m > 0)
        durationStr += $"{m}m ";
      durationStr += $"{s}s";

      var c = "#ffffff66";

      StatusBar.Print($"Level loaded with {successString}{additionalString} <color={c}>in {durationStr}</color>");
    }
    else
    {
      if (failures > 0)
      {
        StatusBar.Print($"This level seems to be invalid (containing {failString} we didn't recognize).");
      }
      else
      {
        StatusBar.Print("Loading failed because the level seems to be empty.");
      }
    }
  }


  public void ClearGrid(bool beginAndEndBatch = true, bool recomputeBounds = true)
  {
    if (beginAndEndBatch)
      BeginBatch("Clear Grid");

    foreach (var kvp in m_Grid)
    {
      // Record the removal of this tile
      OperationSystem.AddDelta(kvp.Value, null);
      Destroy(kvp.Value.m_GameObject);
    }
    m_Grid.Clear();

    if (beginAndEndBatch)
      EndBatch();

    if (recomputeBounds)
      RecomputeBounds();
  }


  // Clears the level and OperationSystem
  public void ForceClearGrid()
  {
    foreach (var kvp in m_Grid)
    {
      Destroy(kvp.Value.m_GameObject);
    }
    m_Grid.Clear();

    RecomputeBounds();

    OperationSystem.ClearOperations();
  }


  public void BeginBatch(string name = "Operation", bool incrementOperationCounter = true)
  {
    if (OperationSystem.s_Frozen)
      return;

    OperationSystem.BeginOperation(name, incrementOperationCounter);
    m_BatchedIndices = new List<Vector2Int>();
  }


  public void AddRequest(Vector2Int gridIndex, TileType tileType, bool cloning = true,
    bool checkUniqueness = true, bool recomputeBounds = true)
  {
    var state = new TileState
    {
      Type = tileType
    };

    AddRequest(gridIndex, state, cloning, checkUniqueness, recomputeBounds);
  }


  public void AddRequest(Vector2Int gridIndex, TileState state, bool cloning = true,
    bool checkUniqueness = true, bool recomputeBounds = true)
  {
    if (checkUniqueness)
    {
      var unique = ConfirmUniqueness(gridIndex);
      if (!unique)
        return;

      m_BatchedIndices.Add(gridIndex);
    }

    AddRequestHelper(gridIndex, state, cloning, recomputeBounds);
  }


  void AddRequestHelper(Vector2Int gridIndex, TileState state, bool cloning,
    bool recomputeBounds)
  {
    // Get now always returns an element, it just might be empty
    var oldElement = Get(gridIndex);
    var tileType = state.Type;

    // here is where i would put the code that would ignore a
    // request for an identical tile to the one already present
    // IF IT WERE POSSIBLE >:(((
    // 
    // the problem is that you should be able to replace one
    // coded tile with another of a different code, but we don't
    // yet know the code of the incoming tile, and we won't know
    // it until the user has selected it from the modal dialog
    // that we haven't made yet. so just forget about it already

    // are we erasing?
    if (tileType == TileType.EMPTY)
    {
      // if the requested index is already empty, forget it
      if (oldElement.m_Type == TileType.EMPTY)
        return;

      // newElement is null
      OperationSystem.AddDelta(oldElement, null);
      EraseTile(gridIndex);

      if (recomputeBounds)
        RecomputeBoundsGivenRemovalAtIndex(gridIndex);
    }
    else
    {
      // are we replacing or just creating?
      if (oldElement.m_Type == TileType.EMPTY)
      {
        // creating without replacing
        var newElement = CreateTile(gridIndex, state, cloning);
        var tile = newElement.m_GameObject;

        // oldElement is null
        OperationSystem.AddDelta(null, newElement, tile);

        if (recomputeBounds)
          RecomputeBoundsGivenNewIndex(gridIndex);
      }
      else
      {
        // replacing an existing tile
        var oldState = oldElement.ToState();
        EraseTile(gridIndex);
        var newElement = CreateTile(gridIndex, state, cloning);
        var newState = newElement.ToState();
        var tile = newElement.m_GameObject;
        OperationSystem.AddDelta(gridIndex, oldState, newState, tile);

        // there's no need to recompute bounds in this case,
        // even if it's asked for, because we're replacing,
        // so there was necessarily already something here
        // that bounds computation has already accounted for
      }
    }
  }


  public void EndBatch(bool createDialogs = true)
  {
    if (!OperationSystem.s_Frozen)
    {
      var message = "The tile grid was asked to end a batch when " +
        "no batch had been begun! Proceed with caution.";
      Debug.LogError(message);

      return;
    }

    m_BatchedIndices = null;

    // now, any modal dialogs that want to be spawned by the most
    // recently placed tile will be pushed onto a stack. as long as
    // a modal dialog is still up and running, it is free to modify
    // the deltas in the latest operation. when the last one is
    // closed, the ModalDialogMaster will call OperationSystem.EndOperation,
    // and the user will regain control
    // 
    // however, if the latest tile doesn't need to open a dialog
    // window, then we can skip all of that, and we can just call
    // EndOperation here and now

    // the most recently created tile will be null if the last thing
    // the user did was erase something
    if (m_MostRecentlyCreatedTile == null)
    {
      OperationSystem.EndOperation();
      return;
    }

    if (createDialogs)
    {
      var modalDialogAdder = m_MostRecentlyCreatedTile.GetComponent<ModalDialogAdder>();
      if (modalDialogAdder == null)
        OperationSystem.EndOperation();
      else
        modalDialogAdder.RequestDialogsAtTransform();
    }
    else
    {
      OperationSystem.EndOperation();
    }

    // it is understood that the ModalDialogMaster will call
    // EndOperation when the last dialog is closed

    m_MostRecentlyCreatedTile = null;
  }


  Element CreateTile(Vector2Int gridIndex, TileState state, bool cloning)
  {
    var prefab = m_TilesPalette.GetPrefabFromType(state.Type);

    // get the parent, if any, that the new tile will be attached to
    //m_TagToParent.TryGetValue(prefab.tag, out Transform parent);

    m_Roots.TryGetValue(state.Type, out var parent);
    if (parent == null)
      parent = m_Transform;

    // TODO: there might be a reason to manually regenerate the
    // composite collider every time we add or subtract a tile.
    // if so, that should happen here (and in EraseTile)

    // instantiate the tile
    var tileWorldPosition = new Vector3(gridIndex.x, gridIndex.y, m_GridZ);
    var newTile = Instantiate(prefab, tileWorldPosition, Quaternion.identity, parent);

    // fill the grid location
    if (m_Grid.ContainsKey(gridIndex))
    {
      m_Grid[gridIndex].m_GameObject = newTile;
      m_Grid[gridIndex].SetState(state);
      m_Grid[gridIndex].m_GridIndex = gridIndex;
    }
    else
    {
      m_Grid[gridIndex] = new Element(gridIndex, state, newTile);
    }

    // call ColorCode.Set with the TileState's Color value, so
    // that:
    // - newly placed tiles whose color hasn't even been picked yet
    //   will be colored with the default color rather than white
    // - tiles being created via undo/redo state enactment will have
    //   their proper color and letter
    if (newTile.TryGetComponent<ColorCode>(out var colorCode))
      colorCode.Set(state.Color);

    if (newTile.TryGetComponent<TileDirection>(out var tileDirection))
      tileDirection.Set(state.Direction);

    if (state.Path != null && state.Path.Count > 0)
    {
      var pathMover = newTile.AddComponent<PathMover>();
      pathMover.Setup(state.Path);

      if (!newTile.TryGetComponent<Rigidbody2D>(out var rigidbody))
        rigidbody = newTile.AddComponent<Rigidbody2D>();
      rigidbody.isKinematic = true;

      if (newTile.TryGetComponent<Collider2D>(out var collider) && !collider.isTrigger)
      {
        newTile.AddComponent<ContactParent>();
      }
    }

    if (newTile.TryGetComponent<SolidEdgeOutliner>(out var solidEdgeOutliner))
      solidEdgeOutliner.Setup(gridIndex);

    if (!cloning)
      m_MostRecentlyCreatedTile = newTile;

    // TODO: as per my discussion with Mr. Cecci, I should move the data re: the most
    // recently created tile to the BlbTool. In fact, it should store references to ALL
    // the tiles that it creates in a batch. In its own EndBatch function, it then
    // checks to see whether it should create dialogs, and create them if necessary. If
    // it does, it can then look at the tiles it's got stored and set their data from
    // what it gets from the dialogs
    // 
    // TODO: as per my even more recent discussion with Mr. Ellinger, I think it's even
    // wiser to prompt the user for the element data right as they are selecting that
    // tile from the tiles palette. This would remove some of the complication of
    // setting all that stuff up at the moment that a tile is placed

    return m_Grid[gridIndex];
  }


  public void EnactOperationState(TileState state, Vector2Int index)
  {
    var oldElement = Get(index);

    if (state.Type == TileType.EMPTY)
    {
      if (oldElement.m_Type != TileType.EMPTY)
        EraseTile(index);
      else
      {
        // TODO: maybe care about this?
      }
    }
    else
    {
      if (oldElement.m_Type != TileType.EMPTY)
        EraseTile(index);

      CreateTile(index, state, cloning: true);
    }
  }


  public void EraseTile(ToolEvent te, Element oldTile)
  {
    EraseTileHelper(te.GridIndex, oldTile.m_GameObject);
  }

  /**
  * FUNCTION NAME: EraseTile
  * DESCRIPTION  : Deletes a tile based on location.
  * INPUTS       : index - Position to try and delete a tile from.
  * OUTPUTS      : None
  **/
  public void EraseTile(Vector2Int index)
  {
    var oldElement = Get(index);
    EraseTileHelper(index, oldElement.m_GameObject);
  }


  void EraseTileHelper(Vector2Int index, GameObject gameObjectToDestroy)
  {
    if (gameObjectToDestroy.TryGetComponent<SolidEdgeOutliner>(out var solidEdgeOutliner))
      solidEdgeOutliner.Erase(index);

    // destroy the old game object and remove the element from the grid
    Destroy(gameObjectToDestroy);
    m_Grid.Remove(index);
  }

  bool ConfirmUniqueness(Vector2Int gridIndexToCheck)
  {
    foreach (var index in m_BatchedIndices)
      if (index == gridIndexToCheck)
        return false;

    return true;
  }
}


[System.Serializable]
public struct TileState
{
  public TileType Type;
  public TileColor Color;
  public Direction Direction;
  public List<Vector2Int> Path;

  public override readonly bool Equals(object rhsObj)
  {
    if (rhsObj is not TileState)
      return false;

    var rhs = (TileState)rhsObj;

    if (rhs.Path == null && Path != null || rhs.Path != null && Path == null)
      return false;

    var matchingData = Type == rhs.Type && Color == rhs.Color && Direction == rhs.Direction;

    if (rhs.Path == null && Path == null)
      return matchingData;

    if (rhs.Path.Count != Path.Count)
      return false;

    for (var i = 0; i < Path.Count; ++i)
      if (rhs.Path[i] != Path[i])
        return false;

    return matchingData;
  }

  public static bool operator ==(TileState lhs, TileState rhs)
  {
    return lhs.Equals(rhs);
  }

  public static bool operator !=(TileState lhs, TileState rhs)
  {
    return !lhs.Equals(rhs);
  }

  public override readonly int GetHashCode()
  {
    // the bit pattern:
    // tttttttt tttttttt tttttttt ccccccdd
    // t - bit used (or reserved) for TileType
    // c - bit used (or reserved) for TileColor
    // d - bit used for direction
    // 
    // note that i'm reserving more bits for color than we actually use right now,
    // just in case we want to add more later, knowing that
    //   A. we'll definitely never need more than two bits for the four directions
    //   B. we'll almost certainly never need more than 24 bits for tile types

    var maskedType = ((int)Type) & 0b00000000111111111111111111111111;
    var maskedColor = ((int)Color) & 0b00000000000000000000000000111111;
    var maskedDirection = ((int)Direction) & 0b00000000000000000000000000000011;
    var shiftedType = maskedType << 8;
    var shiftedColor = maskedColor << 2;
    // var shiftedDirection = maskedDirection << 0, but we can skip that

    return shiftedType | shiftedColor | maskedDirection;
  }
}
