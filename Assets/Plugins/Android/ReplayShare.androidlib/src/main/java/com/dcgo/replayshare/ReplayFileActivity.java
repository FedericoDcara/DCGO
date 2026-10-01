package com.dcgo.replayshare;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.List;

/**
 * Share a replay file, or copy a user-picked document into app cache.
 * C# polls {@link #hasResult} because this library cannot link UnityPlayer.
 */
public class ReplayFileActivity extends Activity
{
    public static volatile boolean hasResult;
    public static volatile String pendingResult;

    static final int REQUEST_PICK = 44021;
    boolean _pickLaunched;
    boolean _shareArmed;
    boolean _shareWentToBackground;

    public static void clearResult()
    {
        hasResult = false;
        pendingResult = null;
    }

    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);

        String mode = getIntent() != null ? getIntent().getStringExtra("mode") : null;
        if ("share".equals(mode))
        {
            if (!share(getIntent().getStringExtra("path")))
            {
                finish();
            }
            return;
        }

        if ("pick".equals(mode))
        {
            if (savedInstanceState != null)
            {
                _pickLaunched = savedInstanceState.getBoolean("pickLaunched", false);
            }

            if (!_pickLaunched)
            {
                _pickLaunched = true;
                Intent pick = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                pick.addCategory(Intent.CATEGORY_OPENABLE);
                pick.setType("*/*");
                pick.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                startActivityForResult(pick, REQUEST_PICK);
            }
            return;
        }

        finish();
    }

    @Override
    protected void onSaveInstanceState(Bundle outState)
    {
        super.onSaveInstanceState(outState);
        outState.putBoolean("pickLaunched", _pickLaunched);
    }

    @Override
    protected void onPause()
    {
        super.onPause();
        if (_shareArmed)
        {
            _shareWentToBackground = true;
        }
    }

    @Override
    protected void onResume()
    {
        super.onResume();
        if (_shareArmed && _shareWentToBackground)
        {
            finish();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_PICK)
        {
            return;
        }

        String copied = "";
        if (resultCode == RESULT_OK && data != null && data.getData() != null)
        {
            copied = copyPicked(data.getData());
        }

        pendingResult = copied != null ? copied : "";
        hasResult = true;
        finish();
    }

    String copyPicked(Uri uri)
    {
        File dest = new File(getCacheDir(), "import_" + System.currentTimeMillis() + ".dcgoreplay");
        InputStream in = null;
        OutputStream out = null;
        try
        {
            in = getContentResolver().openInputStream(uri);
            if (in == null)
            {
                return "";
            }

            out = new FileOutputStream(dest);
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0)
            {
                out.write(buf, 0, n);
            }
            out.flush();
            return dest.getAbsolutePath();
        }
        catch (Exception ex)
        {
            return "";
        }
        finally
        {
            try
            {
                if (in != null) in.close();
            }
            catch (Exception ignored) { }
            try
            {
                if (out != null) out.close();
            }
            catch (Exception ignored) { }
        }
    }

    boolean share(String path)
    {
        if (path == null || path.length() == 0)
        {
            return false;
        }

        try
        {
            File src = new File(path);
            if (!src.isFile())
            {
                return false;
            }

            File dest = new File(getCacheDir(), src.getName());
            if (!src.getCanonicalPath().equals(dest.getCanonicalPath()))
            {
                copyFile(src, dest);
            }

            Uri uri = new Uri.Builder()
                .scheme("content")
                .authority(ReplayStreamProvider.authority(this))
                .path(dest.getAbsolutePath())
                .build();

            Intent send = new Intent(Intent.ACTION_SEND);
            send.setType("application/octet-stream");
            send.putExtra(Intent.EXTRA_STREAM, uri);
            send.putExtra(Intent.EXTRA_SUBJECT, dest.getName());
            send.setClipData(ClipData.newRawUri("replay", uri));
            send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);

            List<android.content.pm.ResolveInfo> handlers = getPackageManager().queryIntentActivities(send, 0);
            for (int i = 0; i < handlers.size(); i++)
            {
                String packageName = handlers.get(i).activityInfo.packageName;
                grantUriPermission(packageName, uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
            }

            Intent chooser = Intent.createChooser(send, "Export replay");
            chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            _shareArmed = true;
            startActivity(chooser);
            return true;
        }
        catch (Exception ignored)
        {
            return false;
        }
    }

    static void copyFile(File src, File dest) throws Exception
    {
        InputStream in = null;
        OutputStream out = null;
        try
        {
            in = new FileInputStream(src);
            out = new FileOutputStream(dest);
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0)
            {
                out.write(buf, 0, n);
            }
            out.flush();
        }
        finally
        {
            if (in != null) in.close();
            if (out != null) out.close();
        }
    }
}
