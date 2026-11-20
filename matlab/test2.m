clc; clear; close all;

%% ===== 1. DLL 路径 =====
% 如果 dll 和 h 文件与本 m 文件在同一个文件夹，可以直接这样写
dllPath = 'USBDAQ_DLL_V12X64.dll';
hPath   = 'HKDAQ_MATLAB.h';

% 如果不在同一个文件夹，改成绝对路径，例如：
% dllPath = 'D:\HK_USB_DAQ\64位库\usb_card_V12.dll';
% hPath   = 'D:\HK_USB_DAQ\64位库\usb_card_V12_dll.h';

libName = 'HKDAQ';

%% ===== 端口定义 =====
PUL_DO = 0;   % PWM1 / OUT1 对应 DoSetV12 通道0
DIR_DO = 2;   % OUT3 对应 DoSetV12 通道2

%% ===== 测试参数 =====
pulseNum = 200;       % 输出200个脉冲
pulseDelay = 0.02;    % 每个半周期20ms，约25Hz，很慢，方便观察
dirState = 0;         % 方向，0或1都可以试

%% ===== 加载DLL =====
if libisloaded(libName)
    unloadlibrary(libName);
end

loadlibrary(dllPath, hPath, 'alias', libName);
disp('DLL加载成功');

ret = calllib(libName, 'OpenUsbV12');
if ret ~= 0
    error('OpenUsbV12 打开失败，返回值 = %d', ret);
end
disp('采集卡打开成功');

cleanupObj = onCleanup(@() safeClose(libName));

%% ===== 共阳极接法下，先让PUL-保持高电平，不导通 =====
calllib(libName, 'DoSetV12', uint8(PUL_DO), uint8(1));

%% ===== 设置方向 =====
calllib(libName, 'DoSetV12', uint8(DIR_DO), uint8(dirState));
pause(0.1);

fprintf('开始手动输出 %d 个低速脉冲...\n', pulseNum);

%% ===== 手动发脉冲 =====
for i = 1:pulseNum
    % 共阳极：PUL+接5V，PUL-拉低时光耦导通
    calllib(libName, 'DoSetV12', uint8(PUL_DO), uint8(0));  % 有效脉冲
    pause(pulseDelay);

    calllib(libName, 'DoSetV12', uint8(PUL_DO), uint8(1));  % 释放
    pause(pulseDelay);
end

disp('脉冲输出完成');

%% ===== 换方向再试一次 =====
pause(1);

dirState = 1 - dirState;
calllib(libName, 'DoSetV12', uint8(DIR_DO), uint8(dirState));
pause(0.1);

fprintf('换方向，再输出 %d 个低速脉冲...\n', pulseNum);

for i = 1:pulseNum
    calllib(libName, 'DoSetV12', uint8(PUL_DO), uint8(0));
    pause(pulseDelay);

    calllib(libName, 'DoSetV12', uint8(PUL_DO), uint8(1));
    pause(pulseDelay);
end

disp('测试完成');

%% ===== 关闭 =====
calllib(libName, 'DoSetV12', uint8(PUL_DO), uint8(1));
calllib(libName, 'CloseUsbV12');
unloadlibrary(libName);

function safeClose(libName)
    try
        if libisloaded(libName)
            calllib(libName, 'DoSetV12', uint8(0), uint8(1));
            calllib(libName, 'CloseUsbV12');
            unloadlibrary(libName);
        end
    catch
    end
end