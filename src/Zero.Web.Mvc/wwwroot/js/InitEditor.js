﻿var frEditorBaseConfig = {
    key: "AV:4~?3xROKLJKYHROLDXDR@d2YYGR_Bc1A8@5@4:1B2D2F2F1?1?2A3@1C1",
    enter: FroalaEditor.ENTER_DIV,
    attribution: false,
    charCounterCount: false,
    toolbarButtons: {
        'moreText': {
            'buttons': ['bold', 'italic', 'underline', 'strikeThrough', 'subscript', 'superscript', 'fontFamily', 'fontSize', 'textColor', 'backgroundColor', 'inlineClass', 'inlineStyle', 'clearFormatting']
        },
        'moreParagraph': {
            'buttons': ['alignLeft', 'alignCenter', 'formatOLSimple', 'alignRight', 'alignJustify', 'formatOL', 'formatUL', 'paragraphFormat', 'paragraphStyle', 'lineHeight', 'outdent', 'indent', 'quote']
        },
        'moreRich': {
            'buttons': ['insertLink', 'insertImage', 'insertVideo', 'insertFile', 'insertTable', 'emoticons', 'fontAwesome', 'specialCharacters', 'embedly', 'insertHR']
        },
        'moreMisc': {
            'buttons': ['undo', 'redo', 'fullscreen', 'print', 'getPDF', 'spellChecker', 'selectAll', 'html', 'help'],
            'align': 'right',
            'buttonsVisible': 2
        }
    },
    imageUploadURL: '/FroalaApi/UploadImage',
    imageUploadParams: {
        __RequestVerificationToken: abp.security.antiForgery.getToken()
    },
    fileUploadURL: '/FroalaApi/UploadFile',
    imageManagerLoadURL: '/FroalaApi/LoadImages',
    imageManagerDeleteURL: "/FroalaApi/DeleteImage",
    imageManagerDeleteMethod: "POST",
    imageManagerDeleteParams: {
        __RequestVerificationToken: abp.security.antiForgery.getToken()
    },
    // Introduce the Video Upload Buttons
    videoInsertButtons: ['videoBack', '|', 'videoByURL', 'videoEmbed', 'videoUpload'],
    // Set the video upload URL.
    videoUploadURL: '/FroalaApi/UploadVideo',
    videoUploadParams: {
        __RequestVerificationToken: abp.security.antiForgery.getToken()
    },
    // Set request type.
    videoUploadMethod: 'POST',

    events: {
        // Catch image removal from the editor.
        'image.removed': function ($img) {
            $.ajax({
                // Request method.
                method: "POST",

                // Request URL.
                url: "/FroalaApi/DeleteImage",

                // Request params.
                data: {
                    __RequestVerificationToken: abp.security.antiForgery.getToken(),
                    src: $img.attr('src')
                }
            })
                .done(function (data) {
                    console.log('image was deleted');
                })
                .fail(function (err) {
                    console.log('image delete problem: ' + JSON.stringify(err));
                })
        },
        // Catch image removal from the editor.
        'video.removed': function ($vid) {
            $.ajax({
                // Request method.
                method: "POST",

                // Request URL.
                url: "/FroalaApi/DeleteVideo",

                // Request params.
                data: {
                    __RequestVerificationToken: abp.security.antiForgery.getToken(),
                    src: $vid.attr('src')
                }
            })
                .done(function (data) {
                    console.log('video was deleted');
                })
                .fail(function (err) {
                    console.log('video delete problem: ' + JSON.stringify(err));
                })
        },

        // Catch image removal from the editor.
        'file.unlink': function (link) {
            $.ajax({
                // Request method.
                method: "POST",

                // Request URL.
                url: "/FroalaApi/DeleteFile",

                // Request params.
                data: {
                    __RequestVerificationToken: abp.security.antiForgery.getToken(),
                    src: link.getAttribute('href')
                }
            })
                .done(function (data) {
                    console.log('file was deleted');
                })
                .fail(function (err) {
                    console.log('file delete problem: ' + JSON.stringify(err));
                })
        }
    }
};

var frEditorConfig = $.extend({
    heightMin: 300,
}, frEditorBaseConfig);

var frEditorConfigInline = $.extend({
    heightMin: 300,
    toolbarInline: true,
}, frEditorBaseConfig);

var frEditorConfigSimple = $.extend({
    heightMin: 200,
}, frEditorBaseConfig);

var frEditorConfigSimpleInline = $.extend({
    heightMin: 200,
    toolbarInline: true,
}, frEditorBaseConfig);

let frEditorConfigHide = $.extend({
    toolbarButtons: ['undo', 'redo' , '|', 'bold', 'italic', 'underline'],
    toolbarButtonsXS: ['undo', 'redo' , '-', 'bold', 'italic', 'underline']
}, frEditorBaseConfig);

