package com.dcgo.replayshare;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;

import java.io.File;
import java.io.FileNotFoundException;

/** Serves replay files from this app's cache so the share sheet can read them. */
public class ReplayStreamProvider extends ContentProvider
{
    public static String authority(android.content.Context context)
    {
        return context.getPackageName() + ".replayfileprovider";
    }

    @Override
    public boolean onCreate()
    {
        return true;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException
    {
        if (getContext() == null || uri == null || uri.getPath() == null)
        {
            throw new FileNotFoundException();
        }

        File file;
        try
        {
            file = new File(uri.getPath()).getCanonicalFile();
        }
        catch (Exception ex)
        {
            throw new FileNotFoundException(ex.getMessage());
        }

        File cache;
        try
        {
            cache = getContext().getCacheDir().getCanonicalFile();
        }
        catch (Exception ex)
        {
            throw new FileNotFoundException(ex.getMessage());
        }

        String cachePath = cache.getPath() + File.separator;
        if (!file.getPath().startsWith(cachePath) || !file.getName().endsWith(".dcgoreplay") || !file.isFile())
        {
            throw new FileNotFoundException(file.getPath());
        }

        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder)
    {
        return null;
    }

    @Override
    public String getType(Uri uri)
    {
        return "application/octet-stream";
    }

    @Override
    public Uri insert(Uri uri, ContentValues values)
    {
        return null;
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs)
    {
        return 0;
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs)
    {
        return 0;
    }
}
